using MassTransit;
using Microsoft.EntityFrameworkCore;
using Polly;

namespace TenantStoreApi.Core.Services;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string None = "None";
    public const string Employee = "Employee";
}

public class UserMembershipService : BaseService<UserMembership>
{
    private readonly IPublishEndpoint _publisher;
    private readonly RoleService _roleService;
    private readonly MembershipRoleService _memberRoleService;

    public UserMembershipService(IUnitOfWorkService service, IPublishEndpoint publisher,
        RoleService roleService,
        MembershipRoleService memberRoleService) : base(service)
    {
        _publisher = publisher;
        _roleService = roleService;
        _memberRoleService = memberRoleService;
    }

    // Resolves each role name to its RoleId (System Role first, then this tenant's Custom Roles)
    // -- callers throughout AuthApi/messaging still pass plain role-name strings (invitation
    // payloads, worker messages), so this keeps every one of those call sites unchanged while the
    // storage/enforcement underneath moves to the real Role/Permission graph.
    private async Task<List<MembershipRole>> BuildMembershipRolesAsync(Guid tenantId, IEnumerable<string> roleNames, CancellationToken token)
    {
        var result = new List<MembershipRole>();
        foreach (var roleName in roleNames.Distinct())
        {
            var role = await _roleService.FindByNameAsync(roleName, tenantId, token);
            result.Add(new MembershipRole { Role = roleName, RoleId = role?.Id });
        }
        return result;
    }

    /// <summary>Creates a membership with one or more initial roles.</summary>
    public async Task AddAsync(UserMembership model, IEnumerable<string> roles, CancellationToken token)
    {
        model.Roles = await BuildMembershipRolesAsync(model.TenantId, roles, token);
        await CreateAsync(model, token);
    }

    public async Task<List<UserMembership>> GetMembersAsync(Guid tenantId, CancellationToken token = default)
    {
        return await GetQueryable(x => x.TenantId == tenantId && !x.IsHidden)
            .InternalOnly()
            .Include(x => x.Roles)
            .ToListAsync(token);
    }

    public async Task<List<AccountMemberShipQuery>> GetUserMembersAsync(Guid userId, CancellationToken token = default)
    {
        // Client-portal memberships never become HRIS tenants or HRIS token claims.
        var memberships = await GetQueryable(x => x.UserId == userId)
            .InternalOnly()
            .Include(x => x.Roles)
            .ThenInclude(x => x.RoleRef)
            .ThenInclude(x => x!.RolePermissions)
            .ThenInclude(x => x.Permission)
            .ToListAsync(token);
        return memberships.Select(x => new AccountMemberShipQuery
        {
            UserId = x.UserId,
            Roles = x.RoleNames(),
            Permissions = x.EffectivePermissionCodes(),
            Status = x.Status,
            TenantId = x.TenantId,
            TenantName = x.TenantName,
        }).ToList();
    }

    // Eager-loads the full permission graph -- every mutating method below reads a caller's
    // permissions via this, so this is the one place that Include chain needs to live.
    // Portal-only roles -- a member holding nothing but these can't manage company details.
    private static readonly string[] PortalOnlyRoles = ["Employee"];

    /// <summary>Whether this user may change company-level details (e.g. the company name) --
    /// an active member with at least one role beyond the portal-only ones, the same audience
    /// HRIS lets onto its Company Settings screen.</summary>
    public async Task<bool> CanManageCompanyAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        var member = await GetMemberAsync(userId, tenantId, token);
        return member != null
            && string.Equals(member.Status, "Active", StringComparison.OrdinalIgnoreCase)
            && member.RoleNames().Any(r => !PortalOnlyRoles.Contains(r));
    }

    public async Task<UserMembership?> GetMemberAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        return await Context.Memberships
            .InternalOnly()
            .Include(x => x.Roles)
            .ThenInclude(x => x.RoleRef)
            .ThenInclude(x => x!.RolePermissions)
            .ThenInclude(x => x.Permission)
            .Where(x => x.UserId == userId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(token);
    }

    /// <summary>
    /// Looks up a membership by its own primary key rather than the (UserId, TenantId) pair --
    /// the row a caller is actually editing on a Users/Members screen is a specific membership,
    /// not a user-and-tenant combination, and a membership's own Id is already what the list
    /// endpoint (GetMembers) hands back per row. Still scoped to `tenantId` so a caller can't act
    /// on a membership belonging to a different tenant just by guessing its Id.
    /// </summary>
    public async Task<UserMembership?> GetMemberByIdAsync(Guid membershipId, Guid tenantId, CancellationToken token = default)
    {
        return await Context.Memberships
            .InternalOnly()
            .Include(x => x.Roles)
            .ThenInclude(x => x.RoleRef)
            .ThenInclude(x => x!.RolePermissions)
            .ThenInclude(x => x.Permission)
            .Where(x => x.Id == membershipId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(token);
    }

    public async Task CreateInvitePlaceholderAsync(Guid tenantId, string tenantName,
        string email, IEnumerable<string> roles, 
        string fullName,
        CancellationToken token = default)
    {
        var existing = await GetQueryable(x =>
            x.TenantId == tenantId &&
            x.InvitedEmail == email &&
            x.Status == "Invited")
            .InternalOnly()
            .FirstOrDefaultAsync(token);
        if (existing != null) return;

        await AddAsync(new UserMembership
        {
            TenantId = tenantId,
            TenantName = tenantName,
            FullName=fullName,
            UserId = Guid.Empty,
            InvitedEmail = email,
            Status = "Invited"
        }, roles, token);
    }

    public async Task<UserMembership?> FindPendingInviteAsync(Guid tenantId, string email, CancellationToken token = default)
    {
        return await GetQueryable(x =>
            x.TenantId == tenantId &&
            x.InvitedEmail == email &&
            x.Status == "Invited")
            .InternalOnly()
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(token);
    }

    public async Task ActivateInviteAsync(UserMembership placeholder, Guid userId, CancellationToken token = default)
    {
        placeholder.UserId = userId;
        placeholder.Status = "Active";
        // Kept (not cleared) after activation -- this is the only local copy of the member's
        // email TenantApi has once they're no longer just a pending invite, and MembersController
        // now serves the members list purely from local data (no AuthApi gRPC lookup), so this
        // is what backs that Email column going forward.
        await ModifyAsync(placeholder, token);
    }

    /// <summary>
    /// Single source of truth for "this user is now a member of this tenant" -- shared by the
    /// synchronous ActivateMembership gRPC call (invitation acceptance) and the async UserJoin
    /// event, which still arrives afterwards for the same join. Idempotent so whichever runs
    /// second is a no-op:
    /// - an existing membership is returned as-is, reactivated if it was Revoked/Inactive (a
    ///   fresh invite is only ever sent to someone NOT already Active -- see Auth's
    ///   SendUserInvitationAsync), and any leftover "Invited" placeholder for the same email is
    ///   removed so InvitationAcceptedWorker can't later activate it into a duplicate row;
    /// - otherwise a pending invite placeholder for this email is activated in place;
    /// - otherwise a new membership is created with the given roles (Member if none).
    /// Doesn't commit -- the caller owns the unit of work.
    /// </summary>
    public async Task<UserMembership> JoinAsync(
        Guid userId,
        Guid tenantId,
        string? tenantName,
        string? email,
        string? fullName,
        IReadOnlyCollection<string> roles,
        CancellationToken token = default)
    {
        var pendingInvite = !string.IsNullOrWhiteSpace(email)
            ? await FindPendingInviteAsync(tenantId, email, token)
            : null;

        var existing = await GetMemberAsync(userId, tenantId, token);
        if (existing != null)
        {
            if (existing.Status != "Active")
            {
                existing.Status = "Active";
                await ModifyAsync(existing, token);
            }
            // By key, not by entity -- FindPendingInviteAsync reads no-tracking, and attaching
            // that detached copy would conflict with any tracked instance of the same row.
            if (pendingInvite != null) await RemoveAsync(pendingInvite.Id, token);
            return existing;
        }

        if (pendingInvite != null)
        {
            await ActivateInviteAsync(pendingInvite, userId, token);
            return pendingInvite;
        }

        var membership = new UserMembership
        {
            TenantId = tenantId,
            TenantName = tenantName,
            InvitedEmail = email,
            FullName = fullName,
            UserId = userId,
        };
        await AddAsync(membership, roles.Count > 0 ? roles : [TenantRoles.None], token);
        return membership;
    }

    /// <summary>
    /// The invitation flow's stricter version of <see cref="JoinAsync"/>: an admin who revokes or
    /// removes a pending invite deletes/closes its "Invited" placeholder, and plain JoinAsync
    /// would just create a fresh Active membership for whoever still holds the link.
    /// - AcceptInvite: needs the still-pending placeholder for this email (or a membership that
    ///   is already Active, so a retry after a half-finished first accept still works).
    /// - Replay: re-opening an already accepted invitation; needs a membership that is still
    ///   Active and never reactivates one that was revoked afterwards.
    /// Returns the membership, or null (with <paramref name="denyReason"/>) when refused; nothing
    /// is changed in that case. Doesn't commit.
    /// </summary>
    public async Task<(UserMembership? Membership, string? DenyReason)> AcceptInviteAsync(
        Guid userId,
        Guid tenantId,
        string? tenantName,
        string? email,
        string? fullName,
        IReadOnlyCollection<string> roles,
        bool replay,
        CancellationToken token = default)
    {
        const string revoked = "This invitation is no longer valid.";
        var existing = await GetMemberAsync(userId, tenantId, token);
        var isActive = existing != null
            && string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase);

        if (replay)
            return isActive ? (existing, null) : (null, revoked);

        var pendingInvite = !string.IsNullOrWhiteSpace(email)
            ? await FindPendingInviteAsync(tenantId, email, token)
            : null;
        if (pendingInvite == null && !isActive)
            return (null, revoked);

        return (await JoinAsync(userId, tenantId, tenantName, email, fullName, roles, token), null);
    }

    /// <summary>Grants an additional role to an existing membership, leaving other roles intact.</summary>
    public async Task AddRoleAsync(Guid callerUserId, Guid targetMembershipId, Guid tenantId, string newRole, CancellationToken token = default)
    {
        if (!await _roleService.IsAssignableAsync(newRole, tenantId, token))
            throw new ArgumentException($"Invalid role '{newRole}'.");

        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");

        var target = await GetMemberByIdAsync(targetMembershipId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");

        if (!target.HasRole(newRole))
        {
            await _roleService.EnsureCanGrantAsync(caller, [newRole], tenantId, token);
            var role = await _roleService.FindByNameAsync(newRole, tenantId, token);
            target.Roles.Add(new MembershipRole { UserMembershipId = target.Id, Role = newRole, RoleId = role?.Id });
            await ModifyAsync(target, token);
        }

        await PublishRoleChangedAsync(target.UserId, tenantId, target.RoleNames(), token);
    }

    /// <summary>Revokes a single role from a membership without touching its other roles.</summary>
    public async Task RemoveRoleAsync(Guid callerUserId, Guid targetMembershipId, Guid tenantId, string role, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");

        var target = await GetMemberByIdAsync(targetMembershipId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (callerUserId == target.UserId)
            throw new InvalidOperationException("Cannot change your own role.");
        if (role == TenantRoles.Owner) throw new InvalidOperationException("Cannot remove the Owner role.");
        if (target.Roles.Count <= 1) throw new InvalidOperationException("A member must have at least one role.");

        var existing = target.Roles.FirstOrDefault(r => r.Role == role);
        if (existing != null)
        {
            target.Roles.Remove(existing);
            await ModifyAsync(target, token);
        }

        await PublishRoleChangedAsync(target.UserId, tenantId, target.RoleNames(), token);
    }

    /// <summary>Replaces a member's entire role set in one call (used by the existing role-management UI).</summary>
    public async Task ReplaceRolesAsync(
        Guid callerUserId, Guid targetMembershipId, Guid tenantId, IEnumerable<string> newRoles, CancellationToken token = default)
    {
        var roles = newRoles.Distinct().ToList();
        if (roles.Count == 0)
            throw new ArgumentException("At least one role is required.");

        foreach (var role in roles)
        {
            if (!await _roleService.IsAssignableAsync(role, tenantId, token))
                throw new ArgumentException($"Invalid role '{role}'.");
        }

        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");

        var target = await GetMemberByIdAsync(targetMembershipId, tenantId, token);
        if (target == null)
            throw new KeyNotFoundException("Member not found.");

        if (callerUserId == target.UserId)
            throw new InvalidOperationException("Cannot change your own role.");

        if (target.HasRole(TenantRoles.Owner))
            throw new InvalidOperationException("Cannot change the Owner's role.");

        // Only roles being added are checked: a member can keep a role the caller couldn't grant.
        await _roleService.EnsureCanGrantAsync(caller, roles.Where(r => !target.HasRole(r)), tenantId, token);

        Context.MembershipRoles.RemoveRange(target.Roles);
        await Context.SaveChangesAsync(token);
        var userRoles = new List<MembershipRole>();
        foreach (var role in roles)
        {
            var roleEntity = await _roleService.FindByNameAsync(role, tenantId, token);
            userRoles.Add(new MembershipRole { UserMembershipId = target.Id, Role = role, RoleId = roleEntity?.Id });
        }
        await Context.MembershipRoles.AddRangeAsync(userRoles, token);
        await Context.SaveChangesAsync(token);
        await PublishRoleChangedAsync(target.UserId, tenantId, roles, token);
    }

    /// <summary>Updates a member's status (e.g. Active, Revoked).</summary>
    public async Task UpdateStatusAsync(Guid callerUserId, Guid targetMembershipId, Guid tenantId, string newStatus, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change member status.");

        var target = await GetMemberByIdAsync(targetMembershipId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (callerUserId == target.UserId)
            throw new InvalidOperationException("Cannot change your own status.");
        if (target.HasRole(TenantRoles.Owner))
            throw new InvalidOperationException("Cannot change the Owner's status.");

        var wasPendingInvite = IsPendingInvite(target);
        target.Status = newStatus;
        await ModifyAsync(target, token);

        await _publisher.Publish(new MembershipChanged
        {
            UserId = target.UserId,
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = newStatus
        }, token);
        if (wasPendingInvite && !string.Equals(newStatus, "Active", StringComparison.OrdinalIgnoreCase))
            await PublishInviteRevokedAsync(target, token);

    }

    /// <summary>
    /// System-initiated revoke for a separated employee (no caller, so none of UpdateStatusAsync's
    /// permission checks). Skips the Owner, who is never locked out this way, and a membership
    /// that is already Revoked. Re-granting access stays a manual Security > Users action.
    /// Returns true when the membership was revoked.
    /// </summary>
    public async Task<bool> RevokeForSeparatedEmployeeAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        var target = await GetMemberAsync(userId, tenantId, token);
        if (target == null
            || target.HasRole(TenantRoles.Owner)
            || string.Equals(target.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            return false;

        target.Status = "Revoked";
        await ModifyAsync(target, token);

        await _publisher.Publish(new MembershipChanged
        {
            UserId = userId,
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = "Revoked"
        }, token);
        return true;
    }

    private static bool IsPendingInvite(UserMembership membership) =>
        string.Equals(membership.Status, "Invited", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(membership.InvitedEmail);

    // Lets Auth expire the invitation link now, rather than leaving it redeemable until it times out.
    private Task PublishInviteRevokedAsync(UserMembership placeholder, CancellationToken token) =>
        _publisher.Publish(new InviteRevoked
        {
            TenantId = placeholder.TenantId,
            Email = placeholder.InvitedEmail!,
        }, token);

    private async Task PublishRoleChangedAsync(Guid userId, Guid tenantId, IEnumerable<string> roles, CancellationToken token)
    {
        await _publisher.Publish(new MembershipChanged
        {
            UserId = userId,
            TenantId = tenantId,
            ChangeType = "RoleChanged",
            NewRoles = roles.ToList()
        }, token);
    }

    public async Task RemoveMemberAsync(Guid callerUserId, Guid targetMembershipId, Guid tenantId, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Delete"))
            throw new UnauthorizedAccessException("Only Owner or Admin can remove members.");
        var target = await GetMemberByIdAsync(targetMembershipId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (callerUserId == target.UserId)
            throw new InvalidOperationException("Cannot remove yourself. Use leave-tenant instead.");
        if (target.HasRole(TenantRoles.Owner)) throw new InvalidOperationException("Cannot remove the Owner.");

        var wasPendingInvite = IsPendingInvite(target);
        await RemoveAsync(target);
        await _publisher.Publish(new MembershipChanged
        {
            UserId = target.UserId,
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = "Revoked"
        }, token);
        if (wasPendingInvite) await PublishInviteRevokedAsync(target, token);

    }

    public async Task LeaveTenantAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        var membership = await GetMemberAsync(userId, tenantId, token);
        if (membership == null) throw new KeyNotFoundException("You are not a member of this tenant.");
        if (membership.HasRole(TenantRoles.Owner))
            throw new InvalidOperationException("Owner cannot leave the tenant. Transfer ownership first.");
        await RemoveAsync(membership);
        await _publisher.Publish(new MembershipChanged
        {
            UserId = userId,
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = "Revoked"
        }, token);
    }
}
