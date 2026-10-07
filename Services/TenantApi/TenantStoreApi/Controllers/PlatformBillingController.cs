using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services.Subscriptions;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

/// <summary>
/// The platform (SaaS operator) side of billing -- what the separate Platform Admin Console will
/// call: plans and prices, an account's plan, trial extensions, overrides, discounts, manual
/// payments and billing history. Accounts are addressed by their owner's user id. Every action needs a
/// platform operator holding a specific permission (<see cref="PlatformAuthorizeAttribute"/>) -- a company
/// user's token is rejected, and there is no shared key any more.
/// </summary>
[Route("api/v{version:apiVersion}/platform/billing")]
[ApiVersion("1.0")]
[ApiController]
public class PlatformBillingController : ControllerBase
{
    private readonly AccountSubscriptionService _subscriptions;
    private readonly SubscriptionViewService _views;
    private readonly PlanCatalogService _plans;
    private readonly BillingEventService _events;

    public PlatformBillingController(
        AccountSubscriptionService subscriptions,
        SubscriptionViewService views,
        PlanCatalogService plans,
        BillingEventService events)
    {
        _subscriptions = subscriptions;
        _views = views;
        _plans = plans;
        _events = events;
    }

    /// <summary>Who did it, for the billing history: the signed-in operator, from their token.</summary>
    private string Actor => PlatformActor.From(User);
    // ── Plans ────────────────────────────────────────────────────────────────────────────────

    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [HttpGet("plans")]
    [ProducesResponseType(typeof(List<PlanView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken token) =>
        Ok((await _plans.GetAllAsync(token)).Select(SubscriptionViewService.ToView).ToList());

    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [HttpPatch("plans/{code}")]
    [ProducesResponseType(typeof(PlanView), StatusCodes.Status200OK)]
    public Task<IActionResult> UpdatePlan(string code, [FromBody] UpdatePlanCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var plan = await _plans.UpdateAsync(code, command, Actor, token);
            await _plans.CommitChangesAsync(token);
            return Ok(SubscriptionViewService.ToView(plan));
        });

    [PlatformAuthorize(PlatformPermissions.PlansManage)]
    [HttpPut("plans/{code}/entitlements/{key}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> SetPlanEntitlement(string code, string key, [FromBody] SetValueRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _plans.SetEntitlementAsync(code, key, request.Value, Actor, token);
            await _plans.CommitChangesAsync(token);
            return Ok();
        });

    // ── Accounts ─────────────────────────────────────────────────────────────────────────────

    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [HttpGet("accounts/{ownerUserId:guid}")]
    [ProducesResponseType(typeof(BillingView), StatusCodes.Status200OK)]
    public Task<IActionResult> GetAccount(Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await _views.GetBillingForAccountAsync(ownerUserId, token)));

    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [HttpGet("accounts/{ownerUserId:guid}/events")]
    [ProducesResponseType(typeof(List<BillingEvent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents(Guid ownerUserId, [FromQuery] int take, CancellationToken token) =>
        Ok(await _events.ListAsync(ownerUserId, take > 0 ? take : 100, token));

    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [HttpGet("accounts/{ownerUserId:guid}/overrides")]
    [ProducesResponseType(typeof(List<SubscriptionOverride>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetOverrides(Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var subscription = await _subscriptions.FindAsync(ownerUserId, token)
                ?? throw new KeyNotFoundException("This account has no subscription.");
            return Ok(subscription.Overrides.Select(o => new
            {
                o.Id, o.TenantId, o.Key, o.Value, o.Reason, o.ExpiresAtUtc, o.CreatedBy, o.CreatedAt,
            }).ToList());
        });

    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [HttpPut("accounts/{ownerUserId:guid}/plan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> SetPlan(Guid ownerUserId, [FromBody] SetPlanCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _subscriptions.SetPlanAsync(ownerUserId, command, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });

    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [HttpPost("accounts/{ownerUserId:guid}/trial-extension")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> ExtendTrial(Guid ownerUserId, [FromBody] ExtendTrialRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _subscriptions.ExtendTrialAsync(ownerUserId, request.Days, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });

    [PlatformAuthorize(PlatformPermissions.PaymentsRecord)]
    [HttpPost("accounts/{ownerUserId:guid}/payments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> RecordPayment(Guid ownerUserId, [FromBody] RecordPaymentCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var payment = await _subscriptions.RecordPaymentAsync(ownerUserId, command, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok(new { payment.Id, payment.PeriodStartUtc, payment.PeriodEndUtc });
        });

    /// <summary>Grant, revoke or change an entitlement for the account, or one of its companies
    /// (TenantId). E.g. { "key": "analytics", "value": "false" } takes Analytics away from a
    /// Premium account.</summary>
    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [HttpPost("accounts/{ownerUserId:guid}/overrides")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> AddOverride(Guid ownerUserId, [FromBody] AddOverrideCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var entry = await _subscriptions.AddOverrideAsync(ownerUserId, command, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok(new { entry.Id });
        });

    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [HttpDelete("accounts/{ownerUserId:guid}/overrides/{overrideId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> RemoveOverride(Guid ownerUserId, Guid overrideId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _subscriptions.RemoveOverrideAsync(ownerUserId, overrideId, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });

    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [HttpPost("accounts/{ownerUserId:guid}/discounts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> AddDiscount(Guid ownerUserId, [FromBody] AddDiscountCommand command, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            var discount = await _subscriptions.AddDiscountAsync(ownerUserId, command, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok(new { discount.Id });
        });

    [PlatformAuthorize(PlatformPermissions.SubscriptionsManage)]
    [HttpDelete("accounts/{ownerUserId:guid}/discounts/{discountId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> RemoveDiscount(Guid ownerUserId, Guid discountId, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _subscriptions.RemoveDiscountAsync(ownerUserId, discountId, Actor, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });
}

public record SetValueRequest
{
    public string Value { get; set; } = string.Empty;
}

public record ExtendTrialRequest
{
    public int Days { get; set; }
}
