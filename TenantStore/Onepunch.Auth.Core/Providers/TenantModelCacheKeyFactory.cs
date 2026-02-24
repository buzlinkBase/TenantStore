using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace OnePunch.Auth.Core.Providers;

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var provider = context.GetService<ITenantProvider>();
        return (context.GetType(), provider?.TenantId ?? Guid.Empty, designTime);
    }
}