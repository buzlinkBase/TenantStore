using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services;

/// <summary>
/// Who is acting on a client's members or roles. The services below never read a token: the
/// entry point (an agency-staff controller today, the portal's service endpoint later) decides who
/// the caller is and hands it in, so the rules live in one place whichever door is used.
/// </summary>
public sealed record ClientCaller(Guid UserId, bool IsAgencyStaff)
{
    /// <summary>A member of the company that owns the client, already verified to hold clientportal:Client:Manage.</summary>
    public static ClientCaller Agency(Guid userId) => new(userId, true);

    /// <summary>A client-portal user; what they may do comes from their roles within that client.</summary>
    public static ClientCaller ClientMember(Guid userId) => new(userId, false);
}

/// <summary>What a caller may do for one client. Agency staff are unrestricted (their permission was
/// checked at the door); a client member holds exactly the codes their roles carry.</summary>
public sealed record ClientCallerScope(bool IsAgencyStaff, IReadOnlySet<string> Codes)
{
    public bool Holds(string code) => IsAgencyStaff || Codes.Contains(code);
}

/// <summary>One person's standing within one client: the roles they hold there and the permission
/// codes those carry. Permissions are empty unless <see cref="Status"/> is Active, so a caller that
/// forgets to check the status still fails closed.</summary>
public sealed record ClientAccess(
    Guid ClientEntityId,
    string ClientName,
    Guid TenantId,
    string? TenantName,
    string Status,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public bool IsActive => Status == StatusActive;

    public const string StatusActive = "Active";
}

/// <summary>
/// Reads and authorization for client-portal access: resolving what a user may do within a client,
/// and deciding whether a caller may manage that client's people and roles.
/// </summary>
public class ClientAccessService
{
    /// <summary>Held by company staff who run the client portal; lets them manage any of their clients.</summary>
    public const string AgencyManageCode = "clientportal:Client:Manage";

    private readonly IUnitOfWorkService _uow;
    private readonly UserMembershipService _staffMemberships;

    public ClientAccessService(IUnitOfWorkService uow, UserMembershipService staffMemberships)
    {
        _uow = uow;
        _staffMemberships = staffMemberships;
    }

    private TenantContext Context => _uow.Context;

    public async Task<ClientEntity> GetEntityAsync(Guid clientEntityId, CancellationToken token = default) =>
        await Context.ClientEntities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == clientEntityId, token)
        ?? throw new KeyNotFoundException("Client not found.");

    /// <summary>Loads the memberships of ClientUser people with the full role-to-permission graph.</summary>
    internal IQueryable<UserMembership> ClientMembershipsWithRoles() =>
        Context.Memberships
            .Where(m => m.Category == MembershipCategory.ClientUser)
            .Include(m => m.ClientRoles).ThenInclude(r => r.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission);

    /// <summary>What <paramref name="userId"/> may do within a client, or null when they have no standing there.</summary>
    public async Task<ClientAccess?> ResolveAsync(Guid userId, Guid clientEntityId, CancellationToken token = default)
    {
        var entity = await Context.ClientEntities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == clientEntityId, token);
        if (entity == null) return null;

        var membership = await ClientMembershipsWithRoles()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == entity.TenantId, token);
        return membership == null ? null : ToAccess(membership, entity);
    }

    /// <summary>Every client this user belongs to, across companies (the portal's client picker).</summary>
    public async Task<List<ClientAccess>> ListForUserAsync(Guid userId, CancellationToken token = default)
    {
        var memberships = await ClientMembershipsWithRoles().Where(m => m.UserId == userId).ToListAsync(token);
        var clientIds = memberships.SelectMany(m => m.ClientRoles).Select(r => r.ClientEntityId).Distinct().ToList();
        var entities = await Context.ClientEntities.AsNoTracking()
            .Where(e => clientIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, token);

        return memberships
            .SelectMany(m => m.ClientRoles.Select(r => r.ClientEntityId).Distinct()
                .Where(entities.ContainsKey)
                .Select(id => ToAccess(m, entities[id])!))
            .OrderBy(a => a.ClientName)
            .ToList();
    }

    internal static ClientAccess? ToAccess(UserMembership membership, ClientEntity entity)
    {
        var links = membership.ClientRoles.Where(r => r.ClientEntityId == entity.Id).ToList();
        if (links.Count == 0) return null;

        // Any one of the four layers being off switches the person off for this client.
        var status = membership.Status != ClientAccess.StatusActive ? membership.Status
            : entity.Status != ClientAccess.StatusActive ? "Inactive"
            : links.Select(l => l.Status).FirstOrDefault(s => s != ClientAccess.StatusActive) ?? ClientAccess.StatusActive;

        var active = status == ClientAccess.StatusActive;
        return new ClientAccess(
            entity.Id,
            entity.Name,
            entity.TenantId,
            membership.TenantName,
            status,
            links.Select(l => l.Role.Name).Distinct().Order().ToList(),
            active
                ? links.SelectMany(l => l.Role.RolePermissions.Select(rp => rp.Permission.Code)).Distinct().Order().ToList()
                : []);
    }

    /// <summary>
    /// Confirms an HRIS caller (token tenant + user) may run this client's access: an active member
    /// of the company that owns it, holding clientportal:Client:Manage. Used by the staff controllers.
    /// </summary>
    public async Task<ClientCaller> AgencyCallerAsync(Guid userId, Guid tenantId, Guid clientEntityId, CancellationToken token = default)
    {
        var entity = await GetEntityAsync(clientEntityId, token);
        // Same answer as a missing client so another company's client ids can't be probed.
        if (entity.TenantId != tenantId) throw new KeyNotFoundException("Client not found.");

        var staff = await _staffMemberships.GetMemberAsync(userId, tenantId, token);
        if (staff == null || staff.Status != ClientAccess.StatusActive || !staff.HasPermission(AgencyManageCode))
            throw new UnauthorizedAccessException("You can't manage this client's users.");
        return ClientCaller.Agency(userId);
    }

    /// <summary>
    /// Works out who <paramref name="userId"/> is to this client when only their id is known (AuthApi
    /// asking on an invitation): company staff running the portal, else a client member. Anyone
    /// else has no standing and is refused.
    /// </summary>
    public async Task<ClientCaller> ResolveCallerAsync(Guid userId, ClientEntity entity, CancellationToken token = default)
    {
        var staff = await _staffMemberships.GetMemberAsync(userId, entity.TenantId, token);
        if (staff is { Status: ClientAccess.StatusActive } && staff.HasPermission(AgencyManageCode))
            return ClientCaller.Agency(userId);

        var access = await ResolveAsync(userId, entity.Id, token);
        if (access is { IsActive: true }) return ClientCaller.ClientMember(userId);
        throw new UnauthorizedAccessException("You don't have access to this client.");
    }

    /// <summary>
    /// Authorizes <paramref name="caller"/> for one client, requiring any one of <paramref name="anyOf"/>
    /// from a client member. Returns what the caller holds so the operation can apply the grant ceiling.
    /// </summary>
    public async Task<ClientCallerScope> AuthorizeAsync(ClientCaller caller, ClientEntity entity, CancellationToken token, params string[] anyOf)
    {
        if (caller.IsAgencyStaff) return new ClientCallerScope(true, new HashSet<string>());

        var access = await ResolveAsync(caller.UserId, entity.Id, token);
        if (access is not { IsActive: true } || !anyOf.Any(access.Permissions.Contains))
            throw new UnauthorizedAccessException("You don't have permission to do that for this client.");
        return new ClientCallerScope(false, access.Permissions.ToHashSet());
    }
}
