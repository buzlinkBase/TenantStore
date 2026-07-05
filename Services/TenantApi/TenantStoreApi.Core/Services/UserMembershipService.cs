using Microsoft.EntityFrameworkCore;

namespace TenantStoreApi.Core.Services;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly string[] Assignable = [Admin, Member];
    public static bool IsValid(string role) => Assignable.Contains(role);
}

public class UserMembershipService : BaseService<UserMembership>
{
    public UserMembershipService(IUnitOfWorkService service) : base(service)
    {
    }

    public async Task AddAsync(UserMembership model, CancellationToken token)
    {
        await CreateAsync(model, token);
    }

    public bool CanAccess(Guid userId, Guid targetTenantId)
    {
        return Context.Memberships.Any(m => m.UserId == userId && m.TenantId == targetTenantId);
    }

    public async Task<List<UserMembership>> GetMembersAsync(Guid tenantId, CancellationToken token = default)
    {
        return await GetQueryable(x => x.TenantId == tenantId).ToListAsync(token);
    }

    public async Task<List<AccountMemberShipQuery>> GetUserMembersAsync(Guid userId, CancellationToken token = default)
    {
        return await GetQueryable(x => x.UserId == userId)
            .Select(x => new AccountMemberShipQuery
            {
                UserId = x.UserId,
                Role = x.Role,
                Status = x.Status,
                TenantId = x.TenantId,
                TenantName = x.TenantName,
            })
            .ToListAsync(token);
    }

    public async Task<UserMembership?> GetMemberAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        return await GetQueryable(x => x.UserId == userId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(token);
    }

    public async Task UpdateRoleAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string newRole, CancellationToken token = default)
    {
        if (!TenantRoles.IsValid(newRole))
            throw new ArgumentException($"Invalid role '{newRole}'. Allowed: {string.Join(", ", TenantRoles.Assignable)}.");

        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || (caller.Role != TenantRoles.Owner && caller.Role != TenantRoles.Admin))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot change your own role.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (target.Role == TenantRoles.Owner) throw new InvalidOperationException("Cannot change the Owner's role.");

        target.Role = newRole;
        await ModifyAsync(target, token);
    }

    public async Task RemoveMemberAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || (caller.Role != TenantRoles.Owner && caller.Role != TenantRoles.Admin))
            throw new UnauthorizedAccessException("Only Owner or Admin can remove members.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot remove yourself. Use leave-tenant instead.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (target.Role == TenantRoles.Owner) throw new InvalidOperationException("Cannot remove the Owner.");

        await RemoveAsync(target);
    }

    public async Task LeaveTenantAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        var membership = await GetMemberAsync(userId, tenantId, token);
        if (membership == null) throw new KeyNotFoundException("You are not a member of this tenant.");
        if (membership.Role == TenantRoles.Owner)
            throw new InvalidOperationException("Owner cannot leave the tenant. Transfer ownership first.");
        await RemoveAsync(membership);
    }
}
