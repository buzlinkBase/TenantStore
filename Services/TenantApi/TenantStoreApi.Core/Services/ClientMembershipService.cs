using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;

namespace TenantStoreApi.Core.Services;

public sealed record ClientRoleRef(Guid Id, string Name);

/// <summary>What AuthApi needs to word and store an invitation once it's been authorized.</summary>
public sealed record ClientInviteContext(Guid TenantId, string TenantName, string ClientName);

public sealed record ClientMemberView(
    Guid MembershipId,
    Guid UserId,
    string? FullName,
    string? Email,
    string Status,
    IReadOnlyList<ClientRoleRef> Roles,
    // The person's OWN switch for this client, leaving out whether the client itself is on: while a client is suspended every member's
    // Status is Inactive, and this is what tells someone revoked on their own apart from someone only blocked by the suspension.
    string OwnStatus = ClientAccess.StatusActive);

/// <summary>
/// People of a client (Category=ClientUser memberships) and the roles they hold within it
/// (Client_Membership_Roles). Ports the client portal's own safeguards: a client always keeps an
/// active Client Admin, nobody edits or deactivates themselves, and nobody grants a role carrying
/// permissions they don't hold.
///
/// A person's switch for one client is the Status of their role rows for that client, so
/// deactivating someone at one client leaves them untouched at another.
///
/// Mutations don't commit; the entry point owns the unit of work (CommitChangesAsync).
/// </summary>
public class ClientMembershipService : BaseService<UserMembership>
{
    private const string Active = ClientAccess.StatusActive;
    private const string Inactive = "Inactive";

    private readonly ClientAccessService _access;
    private readonly IPublishEndpoint _publisher;

    public ClientMembershipService(IUnitOfWorkService uow, ClientAccessService access, IPublishEndpoint publisher) : base(uow)
    {
        _access = access;
        _publisher = publisher;
    }

    public async Task<List<ClientMemberView>> ListMembersAsync(ClientCaller caller, Guid clientEntityId, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.UsersView, ClientPermissions.UsersManage, ClientPermissions.UsersAssignRoles);

        var members = await _access.ClientMembershipsWithRoles()
            .Where(m => m.TenantId == entity.TenantId && m.ClientRoles.Any(r => r.ClientEntityId == clientEntityId))
            .ToListAsync(token);

        return members
            .Select(m =>
            {
                var links = m.ClientRoles.Where(r => r.ClientEntityId == clientEntityId).ToList();
                return new ClientMemberView(
                    m.Id,
                    m.UserId,
                    m.FullName,
                    m.InvitedEmail,
                    ClientAccessService.ToAccess(m, entity)?.Status ?? m.Status,
                    links.Select(l => new ClientRoleRef(l.RoleId, l.Role.Name)).OrderBy(r => r.Name).ToList(),
                    m.Status != Active ? m.Status : links.Select(l => l.Status).FirstOrDefault(s => s != Active) ?? Active);
            })
            .OrderBy(m => m.FullName)
            .ToList();
    }

    /// <summary>Replaces the roles a person holds within a client.</summary>
    public async Task SetRolesAsync(ClientCaller caller, Guid clientEntityId, Guid membershipId, IReadOnlyCollection<Guid> roleIds, CancellationToken token = default)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("At least one role is required.");

        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var scope = await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.UsersAssignRoles);
        var target = await LoadTargetAsync(entity, membershipId, token);
        if (caller.UserId == target.UserId) throw new InvalidOperationException("Cannot change your own roles.");

        var roles = await AssignableRolesAsync(entity, ids, token);
        var links = LinksOf(target, clientEntityId);
        EnsureMayManage(scope, links);

        // Only roles being added are checked: a person can keep a role the caller couldn't grant.
        var held = links.Select(l => l.RoleId).ToHashSet();
        EnsureMayGrant(scope, roles.Where(r => !held.Contains(r.Id)));

        var keepsAdmin = roles.Any(IsAdminRole);
        if (!keepsAdmin && links.Any(l => IsAdminRole(l.Role)) && IsActive(target, links))
            await EnsureAnotherActiveAdminAsync(entity, target.Id, token);

        var status = links.Select(l => l.Status).FirstOrDefault() ?? Active;
        Context.ClientMembershipRoles.RemoveRange(links.Where(l => !ids.Contains(l.RoleId)));
        foreach (var role in roles.Where(r => !held.Contains(r.Id)))
            await AddLinkAsync(target.Id, clientEntityId, role.Id, status, token);

        await PublishAsync(target.UserId, entity, "RolesChanged", token);
    }

    /// <summary>Switches a person on or off for one client (Active / Inactive).</summary>
    public async Task SetStatusAsync(ClientCaller caller, Guid clientEntityId, Guid membershipId, string status, CancellationToken token = default)
    {
        if (status != Active && status != Inactive) throw new ArgumentException("Status must be Active or Inactive.");

        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var scope = await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.UsersManage);
        var target = await LoadTargetAsync(entity, membershipId, token);
        var links = LinksOf(target, clientEntityId);
        EnsureMayManage(scope, links);

        if (status == Inactive)
        {
            if (caller.UserId == target.UserId) throw new InvalidOperationException("Cannot deactivate yourself.");
            if (links.Any(l => IsAdminRole(l.Role)) && IsActive(target, links))
                await EnsureAnotherActiveAdminAsync(entity, target.Id, token);
        }

        foreach (var link in links) link.Status = status;
        await PublishAsync(target.UserId, entity, "StatusChanged", token);
    }

    /// <summary>Takes a person out of one client. Their other clients, if any, are untouched.</summary>
    public async Task RemoveFromClientAsync(ClientCaller caller, Guid clientEntityId, Guid membershipId, CancellationToken token = default)
    {
        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var scope = await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.UsersManage);
        var target = await LoadTargetAsync(entity, membershipId, token);
        if (caller.UserId == target.UserId) throw new InvalidOperationException("Cannot remove yourself.");

        var links = LinksOf(target, clientEntityId);
        EnsureMayManage(scope, links);
        if (links.Any(l => IsAdminRole(l.Role)) && IsActive(target, links))
            await EnsureAnotherActiveAdminAsync(entity, target.Id, token);

        Context.ClientMembershipRoles.RemoveRange(links);
        // Nothing left anywhere: the membership has no reason to exist any more.
        if (target.ClientRoles.All(r => r.ClientEntityId == clientEntityId))
            Context.Memberships.Remove(target);

        await PublishAsync(target.UserId, entity, "Removed", token);
    }

    /// <summary>
    /// System-initiated grant (invitation acceptance, data migration -- no caller to authorize): makes
    /// <paramref name="userId"/> a client user of the company, with these roles within the client.
    /// Idempotent: an existing membership is reused and reactivated, roles already held are left alone.
    /// </summary>
    public async Task<UserMembership> GrantAccessAsync(
        Guid userId, Guid clientEntityId, string? email, string? fullName, IReadOnlyCollection<Guid> roleIds, CancellationToken token = default)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("At least one role is required.");

        var entity = await _access.GetEntityAsync(clientEntityId, token);
        var roles = await AssignableRolesAsync(entity, ids, token);

        var membership = await _access.ClientMembershipsWithRoles()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == entity.TenantId, token);
        if (membership == null)
        {
            var tenant = await Context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == entity.TenantId, token);
            membership = new UserMembership
            {
                Id = Guid.CreateVersion7(),
                TenantId = entity.TenantId,
                TenantName = tenant?.TenantName,
                UserId = userId,
                FullName = fullName,
                InvitedEmail = email,
                Category = MembershipCategory.ClientUser,
            };
            await Context.Memberships.AddAsync(membership, token);
        }
        else
        {
            if (membership.Status != Active) membership.Status = Active;
            // The email is what "already belongs" is checked against; fill it in if the row predates it.
            membership.InvitedEmail ??= email;
            membership.FullName ??= fullName;
        }

        var heldLinks = membership.ClientRoles.Where(r => r.ClientEntityId == clientEntityId).ToList();
        var held = heldLinks.Select(r => r.RoleId).ToHashSet();
        // Inviting someone again who was switched off for this client brings them back: the acceptance is the client saying yes.
        foreach (var link in heldLinks.Where(l => l.Status != Active))
            link.Status = Active;
        foreach (var role in roles.Where(r => !held.Contains(r.Id)))
            await AddLinkAsync(membership.Id, clientEntityId, role.Id, Active, token);

        await PublishAsync(userId, entity, "RolesChanged", token);
        return membership;
    }

    /// <summary>
    /// Whether <paramref name="inviterUserId"/> may invite <paramref name="email"/> to a client with these
    /// roles: they need Users:Invite there (company staff running the portal always may), may only hand
    /// out roles they could grant themselves, and the person must not already belong. Answers AuthApi
    /// before it sends an invitation; nothing is written.
    /// </summary>
    public async Task<ClientInviteContext> AuthorizeInviteAsync(
        Guid inviterUserId, Guid clientEntityId, string email, IReadOnlyCollection<Guid> roleIds, CancellationToken token = default)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("At least one role is required.");

        var entity = await _access.GetEntityAsync(clientEntityId, token);
        if (entity.Status != Active) throw new InvalidOperationException("This client is not active.");

        var caller = await _access.ResolveCallerAsync(inviterUserId, entity, token);
        var scope = await _access.AuthorizeAsync(caller, entity, token, ClientPermissions.UsersInvite);
        EnsureMayGrant(scope, await AssignableRolesAsync(entity, ids, token));

        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var alreadyMember = normalized.Length > 0 && await Context.ClientMembershipRoles.AnyAsync(l =>
            l.ClientEntityId == entity.Id
            && l.Status == Active
            && l.UserMembership.Status == Active
            && l.UserMembership.InvitedEmail != null
            && l.UserMembership.InvitedEmail.ToLower() == normalized, token);
        if (alreadyMember) throw new InvalidOperationException("This person already belongs to this client.");

        var tenant = await Context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == entity.TenantId, token);
        return new ClientInviteContext(entity.TenantId, tenant?.TenantName ?? string.Empty, entity.Name);
    }

    // The ceiling shared by changing someone's roles and inviting them: only roles the caller holds
    // every permission of (agency staff hold them all).
    private static void EnsureMayGrant(ClientCallerScope scope, IEnumerable<Role> roles)
    {
        foreach (var role in roles)
        {
            var codes = role.RolePermissions.Select(rp => rp.Permission.Code);
            if (!RoleGrantPolicy.CanGrant(scope.IsAgencyStaff, scope.Codes, role.Name, role.IsSystemRole, codes))
                throw new InvalidOperationException(RoleGrantPolicy.DeniedMessage(role.Name));
        }
    }

    // The same ceiling the other way round, for the one role that runs the client: nobody who lacks everything a Client Admin holds can switch
    // one off, demote or remove them, so a user who can manage users can't lock the client's admins out. Agency staff and other Client Admins
    // (who hold every permission) are unaffected; everyone else keeps managing the people below them as before.
    private static void EnsureMayManage(ClientCallerScope scope, IEnumerable<ClientMembershipRole> targetLinks)
    {
        foreach (var link in targetLinks.Where(l => IsAdminRole(l.Role)))
        {
            var codes = link.Role.RolePermissions.Select(rp => rp.Permission.Code);
            if (!RoleGrantPolicy.CanGrant(scope.IsAgencyStaff, scope.Codes, link.Role.Name, link.Role.IsSystemRole, codes))
                throw new InvalidOperationException(RoleGrantPolicy.DeniedManageMessage(link.Role.Name));
        }
    }

    private static bool IsAdminRole(Role role) => role.IsSystemRole && role.Name == ClientRoles.Admin;

    private static List<ClientMembershipRole> LinksOf(UserMembership membership, Guid clientEntityId) =>
        membership.ClientRoles.Where(r => r.ClientEntityId == clientEntityId).ToList();

    private static bool IsActive(UserMembership membership, List<ClientMembershipRole> links) =>
        membership.Status == Active && links.All(l => l.Status == Active);

    // Through the DbSet, not the membership's collection: a link carries a client-generated key, and
    // one discovered through a tracked parent's navigation is saved as an update of a row that
    // doesn't exist.
    private async Task AddLinkAsync(Guid membershipId, Guid clientEntityId, Guid roleId, string status, CancellationToken token) =>
        await Context.ClientMembershipRoles.AddAsync(new ClientMembershipRole
        {
            Id = Guid.CreateVersion7(),
            UserMembershipId = membershipId,
            ClientEntityId = clientEntityId,
            RoleId = roleId,
            Status = status,
        }, token);

    private async Task<UserMembership> LoadTargetAsync(ClientEntity entity, Guid membershipId, CancellationToken token)
    {
        var target = await _access.ClientMembershipsWithRoles()
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.TenantId == entity.TenantId, token);
        if (target == null || target.ClientRoles.All(r => r.ClientEntityId != entity.Id))
            throw new KeyNotFoundException("Member not found.");
        return target;
    }

    /// <summary>The roles a client may hold: the built-in client roles, or a custom role this client defined.</summary>
    private async Task<List<Role>> AssignableRolesAsync(ClientEntity entity, List<Guid> roleIds, CancellationToken token)
    {
        var roles = await Context.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Where(r => roleIds.Contains(r.Id)
                && r.Audience == RoleAudience.Client
                && (r.IsSystemRole || (r.TenantId == entity.TenantId && r.ClientEntityId == entity.Id)))
            .ToListAsync(token);
        if (roles.Count != roleIds.Count) throw new ArgumentException("Invalid role.");
        return roles;
    }

    private async Task EnsureAnotherActiveAdminAsync(ClientEntity entity, Guid excludingMembershipId, CancellationToken token)
    {
        var another = await Context.ClientMembershipRoles.AnyAsync(l =>
            l.ClientEntityId == entity.Id
            && l.Status == Active
            && l.UserMembershipId != excludingMembershipId
            && l.UserMembership.Status == Active
            && l.Role.IsSystemRole && l.Role.Name == ClientRoles.Admin, token);
        if (!another) throw new InvalidOperationException("A client must keep at least one active Client Admin.");
    }

    private Task PublishAsync(Guid userId, ClientEntity entity, string changeType, CancellationToken token) =>
        _publisher.Publish(new ClientMembershipChanged
        {
            UserId = userId,
            TenantId = entity.TenantId,
            ClientEntityId = entity.Id,
            ChangeType = changeType,
        }, token);
}

/// <summary>Client-side permission codes this service enforces (product-prefixed, see ClientPortalPermissionCatalog).</summary>
public static class ClientPermissions
{
    public const string UsersView = "clientportal:Users:View";
    public const string UsersInvite = "clientportal:Users:Invite";
    public const string UsersAssignRoles = "clientportal:Users:AssignRoles";
    public const string UsersManage = "clientportal:Users:Manage";
    public const string RolesManage = "clientportal:Roles:Manage";
}
