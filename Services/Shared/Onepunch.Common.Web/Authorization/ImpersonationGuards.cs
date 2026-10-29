using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.Security;

namespace Onepunch.Common.Web.Authorization;

/// <summary>
/// Ends an impersonation session the moment the operator stops it (or it is ended for them): the token
/// itself stays cryptographically valid until it expires, so every API asks the shared cache whether
/// its session was ended. Place after authentication. Requests that are not impersonation sessions
/// pass through untouched.
/// </summary>
public sealed class ImpersonationSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICacheService cache)
    {
        var sessionId = context.User.GetImpersonationSessionId();
        if (context.User.IsImpersonating() && (sessionId == null || await cache.GetAsync<bool>(ImpersonationClaims.EndedCacheKey(sessionId.Value))))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}

/// <summary>
/// For services that do not keep impersonation sessions (company/billing administration, notifications):
/// an operator signed in as a user may read there but not change anything. Place after authentication.
/// </summary>
public sealed class ImpersonationReadOnlyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.IsImpersonating() && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

/// <summary>
/// Refuses the action while the caller is a support operator signed in as someone: changing a
/// password, MFA, email or payment details, or deleting an account, must be done by the person themselves.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ForbidWhenImpersonatingAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.User.IsImpersonating()) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = "This can't be done while signed in as another user.",
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

public static class ImpersonationGuardExtensions
{
    public static IApplicationBuilder UseImpersonationReadOnlyGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<ImpersonationReadOnlyMiddleware>();

    public static IApplicationBuilder UseImpersonationSessionGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<ImpersonationSessionMiddleware>();
}
