using Microsoft.AspNetCore.Authorization;
using Onepunch.Auth.Core.Portal;

namespace OnePunch.Auth.Api.Authorization;

/// <summary>
/// Who may call Auth's internal gRPC services (user info, check email, tenant provisioning, account
/// origins, tenant activity): a signed-in user, as before, or another service presenting the shared
/// service key in <c>X-Api-Key</c> (<c>ApiKeySettings:ApiKey</c>). Tenant Service calls several of
/// them with no user at all -- from background jobs, or for a Platform Console operator, whose token
/// Auth doesn't issue -- and the fallback "signed-in user" policy turned every such call into a 401.
/// </summary>
public static class InternalServiceAuthorization
{
    public const string Policy = "InternalService";
    public const string KeyHeader = "X-Api-Key";
}

public sealed class InternalServiceRequirement : IAuthorizationRequirement;

public sealed class InternalServiceHandler(IConfiguration config, IHttpContextAccessor accessor)
    : AuthorizationHandler<InternalServiceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, InternalServiceRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var http = context.Resource as HttpContext ?? accessor.HttpContext;
        var presented = http?.Request.Headers[InternalServiceAuthorization.KeyHeader].ToString();
        if (PortalTokenValidation.KeyMatches(presented, config["ApiKeySettings:ApiKey"] ?? ""))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
