using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TenantStoreApi.Filters;

/// <summary>
/// Placeholder auth for the platform (SaaS operator) API until the Platform Admin Console has its
/// own authentication: header X-Platform-Key must equal config "Platform:ApiKey". Deliberately a
/// different key from the service-to-service one -- it can move plans and record payments. With
/// no key configured, every call is refused.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PlatformApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Platform-Key";
    public const string ActorHeaderName = "X-Platform-Actor";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Platform:ApiKey"];
        var supplied = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(configured) || supplied == null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(supplied)))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }
}
