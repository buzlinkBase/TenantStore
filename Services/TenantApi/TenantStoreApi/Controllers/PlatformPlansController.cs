using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record SetEntitlementsRequest
{
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>A plan as the console lists it: its definition plus how many accounts are on it.</summary>
public sealed record PlatformPlanView(PlanView Plan, int Subscribers);

/// <summary>
/// The plan catalog for the Platform Admin Console: list, create, edit, and set a plan's
/// entitlements in one go. Edits to a plan's entitlements reach every account on it immediately --
/// <c>GET {code}/impact</c> says how many, for the console's confirmation. Plans are never deleted
/// (accounts reference them); a retired plan is simply made not public.
/// </summary>
[Route("api/v{version:apiVersion}/platform/plans")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformPlansController : ControllerBase
{
    private readonly PlanCatalogService _plans;

    public PlatformPlansController(PlanCatalogService plans) => _plans = plans;

    private string Actor => PlatformActor.From(User);

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(List<PlatformPlanView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token)
    {
        var plans = await _plans.GetAllAsync(token);
        var subscribers = await _plans.GetSubscriberCountsAsync(token);
        return Ok(plans.Select(p => new PlatformPlanView(SubscriptionViewService.ToView(p), subscribers.GetValueOrDefault(p.Id))).ToList());
    }

    /// <summary>What every entitlement is called and what it controls, for rendering a plan's entitlements.</summary>
    [HttpGet("catalog")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(IReadOnlyList<EntitlementInfo>), StatusCodes.Status200OK)]
    // A List, not the catalog's own collection: that one is a compiler-generated internal type, which
    // ResponseWrapperFilter cannot wrap (it binds the runtime type dynamically) -- the action would 500.
    public IActionResult Catalog() => Ok(EntitlementCatalog.All.ToList());

    [HttpGet("{code}/impact")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(PlanImpact), StatusCodes.Status200OK)]
    public Task<IActionResult> Impact(string code, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _plans.GetImpactAsync(code, token)));

    [HttpPost]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(typeof(PlatformPlanView), StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] CreatePlanCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var plan = await _plans.CreateAsync(command, Actor, token);
            await _plans.CommitChangesAsync(token);
            return Ok(new PlatformPlanView(SubscriptionViewService.ToView(plan), 0));
        });

    [HttpPatch("{code}")]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(typeof(PlanView), StatusCodes.Status200OK)]
    public Task<IActionResult> Update(string code, [FromBody] UpdatePlanCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var plan = await _plans.UpdateAsync(code, command, Actor, token);
            await _plans.CommitChangesAsync(token);
            return Ok(SubscriptionViewService.ToView(plan));
        });

    [HttpPut("{code}/entitlements")]
    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public Task<IActionResult> SetEntitlements(string code, [FromBody] SetEntitlementsRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var changed = await _plans.SetEntitlementsAsync(code, request.Values, Actor, token);
            await _plans.CommitChangesAsync(token);
            return Ok(changed);
        });
}
