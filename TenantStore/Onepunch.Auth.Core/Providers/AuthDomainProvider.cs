using Microsoft.Extensions.Options; 
namespace Onepunch.Auth.Core.Providers;

public interface IAuthDomainProvider
{
    AuthSettings Resolve();
}

public class AuthDomainProvider : IAuthDomainProvider
{
    private readonly AuthSettings _options;
    public AuthDomainProvider(IOptions<AuthSettings> options)
    {
        _options = options.Value;
    }
    public AuthSettings Resolve() => _options;
}
