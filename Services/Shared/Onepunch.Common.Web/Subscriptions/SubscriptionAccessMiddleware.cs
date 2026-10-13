using BuzlinkRepository;
using Microsoft.AspNetCore.Http;
using Onepunch.Common.Lib.Subscriptions;

namespace Onepunch.Common.Web.Subscriptions;

/// <summary>
/// A lapsed subscription (trial ended, suspended for non-payment, canceled) -- the rule every
/// service applies (<see cref="SubscriptionAccessPolicy"/>):
/// - the owner and billing admins keep read-only access: they can look, export and pay, but every
///   POST/PUT/PATCH/DELETE is refused with 403 "subscription-read-only", unless the endpoint is
///   marked <see cref="AllowWhenReadOnlyAttribute"/> (a POST that only reads);
/// - every other member is locked out of the company: every request, reads included, is refused
///   with 403 "subscription-locked".
/// Enforced here on the backend -- hiding the UI isn't enforcement. Runs after the middleware that
/// resolves the company (the tenant is known). Anonymous and service-to-service calls aren't
/// company users and pass; so do the SignalR hubs.
/// </summary>
public class SubscriptionAccessMiddleware
{
    private readonly RequestDelegate _next;
    public SubscriptionAccessMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider, IEntitlementProvider entitlements)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && !context.Request.Path.StartsWithSegments("/hubs")
            && tenantProvider.TenantId != Guid.Empty)
        {
            var snapshot = await entitlements.GetAsync(tenantProvider.TenantId, context.RequestAborted);
            var decision = SubscriptionAccessPolicy.Decide(
                snapshot, context.Request.Method, BillingAccess.IsBillingUser(context.User));

            // A POST that only reads stays open to those who can read at all.
            if (decision == SubscriptionAccessDecision.ReadOnly
                && context.GetEndpoint()?.Metadata.GetMetadata<AllowWhenReadOnlyAttribute>() != null)
            {
                decision = SubscriptionAccessDecision.Allow;
            }

            if (SubscriptionAccessPolicy.Refusal(decision) is { } refusal) throw refusal;
        }

        await _next(context);
    }
}
