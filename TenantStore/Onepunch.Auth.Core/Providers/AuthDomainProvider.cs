using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options; 
namespace Onepunch.Auth.Core.Providers;

public interface IAuthDomainProvider
{
    Domains Resolve();
}

public class AuthDomainProvider : IAuthDomainProvider
{
    private readonly Domains _options;
    public AuthDomainProvider(IOptions<Domains> options)
    {
        _options = options.Value;
    }
    public Domains Resolve() => _options;
}


public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is AuthContext tenantContext)
        {
            return (context.GetType(), tenantContext.TenantId, designTime);
        }

        return (context.GetType(), designTime);
    }
}
