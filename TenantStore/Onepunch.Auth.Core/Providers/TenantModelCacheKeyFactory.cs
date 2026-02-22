using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace OnePunch.Auth.Core.Providers;

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    private readonly IServiceScopeFactory _factory;
    public TenantModelCacheKeyFactory(IServiceScopeFactory factory)
    {
        _factory = factory;
    }
    public object Create(DbContext context, bool designTime)
    {
        using var scope = _factory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
        return (context.GetType(), provider.TenantId, designTime);
    }
}