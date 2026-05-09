
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services;

public class PlanService : BaseService<Plan>
{
    public PlanService(IUnitOfWorkService service) : base(service)
    {
    }
    public async Task AddAsync(Plan plan, CancellationToken token = default)
    {
        await CreateAsync(plan, token);
        await CommitChangesAsync(token);
    }

    public async Task<Plan?> FindPlanAsync(Guid Id, CancellationToken token = default)
    {
        return await GetOneAsync(Id, token);
    }

    public async Task<Plan?> FindFreeTrialAsync(CancellationToken token = default)
    {
        return await GetQueryable(x => x.Name == "Free Trial").FirstOrDefaultAsync(token);
    }

    //public async Task<bool> IsFeatureEnabled(Guid tenantId, ServiceType feature)
    //{
    //    var sub = await Context.TenantSubscriptions
    //        .Include(s => s.Plan.IncludedServices)
    //        .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.EndDate > DateTime.UtcNow);

    //    return sub?.Plan.IncludedServices.Any(x => x.Service == feature) ?? false;
    //}
}
