using BuzlinkRepository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Core.Filters;

/// <summary>
/// Endpoints a locked-out member still needs: reading the company's entitlements (so the app can
/// show the lock screen), their own company list, leaving a company, and infrastructure calls
/// other services make with a forwarded token.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWhenLockedAttribute : Attribute;

/// <summary>
/// The lapsed-subscription rule (<see cref="SubscriptionAccessPolicy"/>) for Tenant Service's own
/// company endpoints (members, roles, permissions): the owner and billing admins keep read-only
/// access; every other member of a company whose owner's plan lapsed is refused outright.
/// Registered globally; anonymous and service-to-service endpoints aren't company users and pass.
/// </summary>
public sealed class SubscriptionAccessFilter(
    EntitlementResolverService resolver,
    ITenantProvider tenantProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        var user = context.HttpContext.User;
        var tenantId = tenantProvider.TenantId;
        if (user.Identity?.IsAuthenticated != true
            || tenantId == Guid.Empty
            || metadata.OfType<AllowWhenLockedAttribute>().Any()
            || metadata.OfType<IAllowAnonymous>().Any())
        {
            await next();
            return;
        }

        var snapshot = await resolver.ResolveForTenantAsync(tenantId, context.HttpContext.RequestAborted);
        var decision = SubscriptionAccessPolicy.Decide(snapshot, context.HttpContext.Request.Method, BillingAccess.IsBillingUser(user));
        if (SubscriptionAccessPolicy.Refusal(decision) is { } refusal)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = refusal.ProblemType,
                Title = "Not included in your plan",
                Detail = refusal.Message,
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
