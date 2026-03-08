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
}

