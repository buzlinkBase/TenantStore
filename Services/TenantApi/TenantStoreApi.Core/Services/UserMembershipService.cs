using Microsoft.EntityFrameworkCore;

namespace TenantStoreApi.Core.Services;

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

    public async Task<UserMembership?> GetMemberAsync(Guid userId, Guid tenantId, CancellationToken token = default)
    {
        return await GetQueryable(x => x.UserId == userId && x.TenantId == tenantId)
            .FirstOrDefaultAsync(token);
    }

    public async Task UpdateRoleAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, string newRole, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || (caller.Role != "Owner" && caller.Role != "Admin"))
            throw new UnauthorizedAccessException("Only Owner or Admin can change roles.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot change your own role.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (target.Role == "Owner") throw new InvalidOperationException("Cannot change the Owner's role.");

        target.Role = newRole;
        await ModifyAsync(target, token);
    }

    public async Task RemoveMemberAsync(Guid callerUserId, Guid targetUserId, Guid tenantId, CancellationToken token = default)
    {
        var caller = await GetMemberAsync(callerUserId, tenantId, token);
        if (caller == null || (caller.Role != "Owner" && caller.Role != "Admin"))
            throw new UnauthorizedAccessException("Only Owner or Admin can remove members.");
        if (callerUserId == targetUserId)
            throw new InvalidOperationException("Cannot remove yourself.");

        var target = await GetMemberAsync(targetUserId, tenantId, token);
        if (target == null) throw new KeyNotFoundException("Member not found.");
        if (target.Role == "Owner") throw new InvalidOperationException("Cannot remove the Owner.");

        await RemoveAsync(target);
    }
}
