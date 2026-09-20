using MassTransit;
using Microsoft.EntityFrameworkCore;
using Polly;

namespace TenantStoreApi.Core.Services;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
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
            .Include(x => x.Roles)
            .ToListAsync(token);
    }

    public async Task<List<AccountMemberShipQuery>> GetUserMembersAsync(Guid userId, CancellationToken token = default)
    {
        var memberships = await GetQueryable(x => x.UserId == userId)
            .Include(x => x.Roles).ThenInclude(x => x.RoleRef).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
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
    public async Task<UserMembership?> GetMemberAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        return await Context.Memberships
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
            .Include(x => x.Roles)
            .ThenInclude(x => x.RoleRef)
            .ThenInclude(x => x!.RolePermissions)
            .ThenInclude(x => x.Permission)
            .Where(x => x.Id == membershipId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(token);
    }

    public async Task CreateInvitePlaceholderAsync(Guid tenantId, string tenantName, string email, IEnumerable<string> roles, CancellationToken token = default)
    {
        var existing = await GetQueryable(x =>
            x.TenantId == tenantId &&
            x.InvitedEmail == email &&
            x.Status == "Invited")
            .FirstOrDefaultAsync(token);
        if (existing != null) return;

        await AddAsync(new UserMembership
        {
            TenantId = tenantId,
            TenantName = tenantName,
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

        target.Status = newStatus;
        await ModifyAsync(target, token);

        await _publisher.Publish(new MembershipChanged
        {
            UserId = target.UserId,
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = newStatus
        }, token);

    }

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

        await RemoveAsync(target);
        await _publisher.Publish(new MembershipChanged
        {
            UserId = target.UserId,
            TenantId = tenantId,
            ChangeType = "MemberRemoved"
        }, token);
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
            ChangeType = "MemberRemoved"
        }, token);
    }
}
