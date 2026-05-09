namespace TenantStoreApi.Core.Services;

public class TenantDelationService : BaseService<TenantDelegation>
{

    public TenantDelationService(IUnitOfWorkService service) : base(service)
    {
    }

    public async Task Accept(TenantDelegation model, CancellationToken token)
    {
        await CreateAsync(model, token);
    }
}

