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
