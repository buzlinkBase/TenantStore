using MassTransit;
using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.DTO;

namespace TenantStoreApi.Core.Services;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";
    public const string Employee = "Employee";

    public static readonly string[] Assignable = [Admin, Member];
    public static bool IsValid(string role) => Assignable.Contains(role);
}

public class UserMembershipService : BaseService<UserMembership>
{
    private readonly IPublishEndpoint _publisher;

    public UserMembershipService(IUnitOfWorkService service, IPublishEndpoint publisher) : base(service)
    {
        _publisher = publisher;
    }

    /// <summary>Creates a membership with one or more initial roles.</summary>
    public async Task AddAsync(UserMembership model, IEnumerable<string> roles, CancellationToken token)
    {
        model.Roles = roles.Distinct().Select(r => new MembershipRole { Role = r }).ToList();
        await CreateAsync(model, token);
    }

    public bool CanAccess(Guid userId, Guid targetTenantId)
    {
        return Context.Memberships.Any(m => m.UserId == userId && m.TenantId == targetTenantId);
    }

    public async Task<List<UserMembership>> GetMembersAsync(Guid tenantId, CancellationToken token = default)
    {
        return await GetQueryable(x => x.TenantId == tenantId).Include(x => x.Roles).ToListAsync(token);
    }

    public async Task<List<AccountMemberShipQuery>> GetUserMembersAsync(Guid userId, CancellationToken token = default)
    {
        var memberships = await GetQueryable(x => x.UserId == userId).Include(x => x.Roles).ToListAsync(token);
        return memberships.Select(x => new AccountMemberShipQuery
        {
            UserId = x.UserId,
            Roles = x.RoleNames(),
            Status = x.Status,
            TenantId = x.TenantId,
            TenantName = x.TenantName,
        }).ToList();
    }

    public async Task<UserMembership?> GetMemberAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        return await Context.Memberships
            .Include(x => x.Roles)
            .Where(x => x.UserId == userId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(token);
    }

    /// <summary>
    /// Flow B: creates a pending membership placeholder (no UserId yet) when an invite is
    /// sent, so the invitee shows up in the tenant's member list before they ever accept.
    /// </summary>
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

    /// <summary>
    /// Flow B: on invite acceptance, finds the placeholder row created by
    /// CreateInvitePlaceholderAsync so UserJoinWorker can activate it in place rather than
    /// creating a duplicate membership row.
    /// </summary>
    public async Task<UserMembership?> FindPendingInviteAsync(Guid tenantId, string email, CancellationToken token = default)
    {
        return await GetQueryable(x =>
            x.TenantId == tenantId &&
            x.InvitedEmail == email &&
            x.Status == "Invited")
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(token);
    }

    /// <summary>Fills in the real UserId and flips the placeholder to Active on acceptance.</summary>
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
        if (!TenantRoles.IsValid(newRole))
            throw new ArgumentException($"Invalid role '{newRole}'. Allowed: {string.Join(", ", TenantRoles.Assignable)}.");

        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyRole(TenantRoles.Owner, TenantRoles.Admin))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");

        if (!target.HasRole(newRole))
        {
            target.Roles.Add(new MembershipRole { UserMembershipId = target.Id, Role = newRole });
            await ModifyAsync(target, token);
        }

        await PublishRoleChangedAsync(targetUserId, tenantId, target.RoleNames(), token);
    }

    /// <summary>Revokes a single role from a membership without touching its other roles.</summary>
    public async Task RemoveRoleAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string role, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyRole(TenantRoles.Owner, TenantRoles.Admin))
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
    Guid callerUserId,
    Guid targetUserId,
    Guid tenantId,
    IEnumerable<string> newRoles,
    CancellationToken token = default)
    {
        var roles = newRoles.Distinct().ToList();
        if (roles.Count == 0)
            throw new ArgumentException("At least one role is required.");

        foreach (var role in roles)
        {
            if (!TenantRoles.IsValid(role))
                throw new ArgumentException($"Invalid role '{role}'. Allowed: {string.Join(", ", TenantRoles.Assignable)}.");
        }

        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyRole(TenantRoles.Owner, TenantRoles.Admin))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");

        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot change your own role.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null)
            throw new KeyNotFoundException("Member not found.");

        if (target.HasRole(TenantRoles.Owner))
            throw new InvalidOperationException("Cannot change the Owner's role.");

        // --- EF CORE COLLECTION SYNCHRONIZATION FIX ---

        // 1. Remove roles no longer present in 'roles'
        var rolesToRemove = target.Roles
            .Where(r => !roles.Contains(r.Role))
            .ToList();

        foreach (var roleToRemove in rolesToRemove)
        {
            target.Roles.Remove(roleToRemove);
        }

        // 2. Add roles that are not yet assigned to target
        var existingRoleNames = target.Roles.Select(r => r.Role).ToHashSet();
        foreach (var role in roles)
        {
            if (!existingRoleNames.Contains(role))
            {
                target.Roles.Add(new MembershipRole
                {
                    UserMembershipId = target.Id,
                    Role = role
                });
            }
        }

        await ModifyAsync(target, token);
        await PublishRoleChangedAsync(targetUserId, tenantId, roles, token);
    }

    /// <summary>Updates a member's status (e.g. Active, Revoked, Inactive).</summary>
    public async Task UpdateStatusAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string newStatus, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || !caller.HasAnyRole(TenantRoles.Owner, TenantRoles.Admin))
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
        if (caller == null || !caller.HasAnyRole(TenantRoles.Owner, TenantRoles.Admin))
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
