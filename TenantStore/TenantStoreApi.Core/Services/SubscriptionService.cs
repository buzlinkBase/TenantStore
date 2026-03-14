namespace TenantStoreApi.Core.Services;

public class SubscriptionService : BaseService<TenantSubscription>
{
    public SubscriptionService(IUnitOfWorkService service) : base(service)
    {
    }

    public async Task AddAsync(TenantSubscription subscription,CancellationToken token=default)
    {
         await   CreateAsync(subscription, token);
    }

    //public async Task<bool> IsFeatureEnabled(Guid tenantId, ServiceType feature)
    //{
    //    var sub = await Context.TenantSubscriptions
    //        .Include(s => s.Plan.IncludedServices)
    //        .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.EndDate > DateTime.UtcNow);

    //    return sub?.Plan.IncludedServices.Any(x => x.Service == feature) ?? false;
    //}
}
