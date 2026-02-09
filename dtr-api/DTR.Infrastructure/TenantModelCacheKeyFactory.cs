using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DTR.Infrastructure;

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is DTRDbContext tenantContext)
        {
            return (context.GetType(), tenantContext.TenantId, designTime);
        }
        return (context.GetType(), designTime);
    }
}