using MassTransit;
using Microsoft.EntityFrameworkCore;

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

    public UserMembershipService(IUnitOfWorkService service, IPublishEndpoint publisher, RoleService roleService) : base(service)
    {
        _publisher = publisher;
        _roleService = roleService;
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
            .Include(x => x.Roles).ThenInclude(x => x.RoleRef).ThenInclude(x => x!.RolePermissions).ThenInclude(x => x.Permission)
            .Where(x => x.UserId == userId && x.TenantId == tenantId)
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
        placeholder.InvitedEmail = null;
        await ModifyAsync(placeholder, token);
    }

    /// <summary>Grants an additional role to an existing membership, leaving other roles intact.</summary>
    public async Task AddRoleAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string newRole, CancellationToken token = default)
    {
        if (!await _roleService.IsAssignableAsync(newRole, tenantId, token))
            throw new ArgumentException($"Invalid role '{newRole}'.");

        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");

        if (!target.HasRole(newRole))
        {
            var role = await _roleService.FindByNameAsync(newRole, tenantId, token);
            target.Roles.Add(new MembershipRole { UserMembershipId = target.Id, Role = newRole, RoleId = role?.Id });
            await ModifyAsync(target, token);
        }

        await PublishRoleChangedAsync(targetUserId, tenantId, target.RoleNames(), token);
    }

    /// <summary>Revokes a single role from a membership without touching its other roles.</summary>
    public async Task RemoveRoleAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string role, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot change your own role.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (role == TenantRoles.Owner) throw new InvalidOperationException("Cannot remove the Owner role.");
        if (target.Roles.Count <= 1) throw new InvalidOperationException("A member must have at least one role.");

        var existing = target.Roles.FirstOrDefault(r => r.Role == role);
        if (existing != null)
        {
            target.Roles.Remove(existing);
            await ModifyAsync(target, token);
        }

        await PublishRoleChangedAsync(targetUserId, tenantId, target.RoleNames(), token);
    }

    /// <summary>Replaces a member's entire role set in one call (used by the existing role-management UI).</summary>
    public async Task ReplaceRolesAsync(
        Guid callerUserId, Guid targetUserId, Guid tenantId, IEnumerable<string> newRoles, CancellationToken token = default)
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

        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot change your own role.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null)
            throw new KeyNotFoundException("Member not found.");

        if (target.HasRole(TenantRoles.Owner))
            throw new InvalidOperationException("Cannot change the Owner's role.");

        // --- EF CORE COLLECTION SYNCHRONIZATION FIX ---
        var rolesToRemove = target.Roles.Where(r => !roles.Contains(r.Role)).ToList();
        foreach (var roleToRemove in rolesToRemove)
        {
            target.Roles.Remove(roleToRemove);
        }

        var existingRoleNames = target.Roles.Select(r => r.Role).ToHashSet();
        foreach (var role in roles)
        {
            if (!existingRoleNames.Contains(role))
            {
                var roleEntity = await _roleService.FindByNameAsync(role, tenantId, token);
                target.Roles.Add(new MembershipRole { UserMembershipId = target.Id, Role = role, RoleId = roleEntity?.Id });
            }
        }

        await ModifyAsync(target, token);
        await PublishRoleChangedAsync(targetUserId, tenantId, roles, token);
    }

    /// <summary>Updates a member's status (e.g. Active, Revoked, Inactive).</summary>
    public async Task UpdateStatusAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string newStatus, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Edit"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change member status.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot change your own status.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (target.HasRole(TenantRoles.Owner))
            throw new InvalidOperationException("Cannot change the Owner's status.");

        target.Status = newStatus;
        await ModifyAsync(target, token);

        await _publisher.Publish(new MembershipChanged
        {
            UserId = targetUserId,
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

    public async Task RemoveMemberAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyPermission("Tenant Members:Manage", "Users:Delete"))
            throw new UnauthorizedAccessException("Only Owner or Admin can remove members.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot remove yourself. Use leave-tenant instead.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (target.HasRole(TenantRoles.Owner)) throw new InvalidOperationException("Cannot remove the Owner.");

        await RemoveAsync(target);
        await _publisher.Publish(new MembershipChanged
        {
            UserId = targetUserId,
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
