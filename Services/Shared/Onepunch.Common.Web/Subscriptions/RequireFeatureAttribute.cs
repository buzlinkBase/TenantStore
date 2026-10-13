using BuzlinkRepository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Subscriptions;

namespace Onepunch.Common.Web.Subscriptions;

/// <summary>
/// Declarative subscription gate: the action (or every action on the controller) needs the
/// company's plan to include <see cref="Feature"/> -- one of
/// <see cref="EntitlementKeys"/>. Refused with 403 ProblemDetails type "plan-required", which the
/// frontend turns into an upgrade prompt. Stacks with RequirePermissionAttribute:
/// the permission says whether this person may, the feature whether this company's plan does.
///
/// <see cref="WritesOnly"/>: reads pass. For reference data other modules still list in their
/// forms (e.g. Clients on the Employee form) -- locking the setup screen must not break those.
///
/// Usage: [RequireFeature(EntitlementKeys.PayrollCore)] public class PayrollsController ...
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireFeatureAttribute(string feature) : Attribute, IAsyncActionFilter
{
    public string Feature => feature;

    public bool WritesOnly { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (WritesOnly && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            await next();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var tenantId = services.GetRequiredService<ITenantProvider>().TenantId;
        var snapshot = await services.GetRequiredService<IEntitlementProvider>().GetAsync(tenantId, context.HttpContext.RequestAborted);
        if (!snapshot.HasFeature(feature))
        {
            throw PlanRestrictionException.FeatureRequired(feature);
        }

        await next();
    }
}

/// <summary>
/// Lets a POST/PUT through while the subscription is read-only -- for endpoints that only read
/// despite their verb (exports and prints with a filter body). See <see cref="SubscriptionAccessMiddleware"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWhenReadOnlyAttribute : Attribute;
