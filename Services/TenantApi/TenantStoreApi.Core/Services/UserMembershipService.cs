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
        // 1. Check if they are a direct member
        if (Context.Memberships.Any(m => m.UserId == userId && m.TenantId == targetTenantId))
            return true;

        // 2. Check if their HOME tenant has delegation access to the target
        //var userHomeTenantId = _userSession.HomeTenantId;
        //if (Context.TenantDelegations.Any(d => d.GuestTenantId == userHomeTenantId && d.HostTenantId == targetTenantId))
        //    return true;

        return false;
    }
}

