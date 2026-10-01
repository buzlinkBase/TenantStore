using TenantStoreApi.Core.Filters;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Controllers;

/// <summary>
/// The current company's view of its owner account's subscription. Entitlements are for every
/// member (the UI locks features from them); prices, usage and plan changes need Billing:View /
/// Billing:Manage, which the Owner has and Admin deliberately doesn't.
/// </summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
// Reachable when locked: the app reads the entitlements to show the lock screen, the owner pays.
[AllowWhenLocked]
public class SubscriptionsController : ControllerBase
{
    private const string ViewBilling = "Billing:View";
    private const string ManageBilling = "Billing:Manage";

    private readonly SubscriptionViewService _views;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly UserMembershipService _members;
    private readonly TenantService _tenants;
    private readonly ITenantProvider _tenantProvider;

    public SubscriptionsController(
        SubscriptionViewService views,
        AccountSubscriptionService subscriptions,
        UserMembershipService members,
        TenantService tenants,
        ITenantProvider tenantProvider)
    {
        _views = views;
        _subscriptions = subscriptions;
        _members = members;
        _tenants = tenants;
        _tenantProvider = tenantProvider;
    }

    /// <summary>What this company may use right now.</summary>
    [HttpGet("entitlements")]
    [ProducesResponseType(typeof(EntitlementsView), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntitlements(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        if (await _members.GetMemberAsync(User.GetRequiredUserId(), tenantId, token) == null) return Forbid();

        return Ok(await _views.GetEntitlementsAsync(tenantId, token));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(BillingView), StatusCodes.Status200OK)]
    public Task<IActionResult> GetCurrent(CancellationToken token) =>
        WithBillingAccess(ViewBilling, token, async tenantId => Ok(await _views.GetBillingForTenantAsync(tenantId, token)));

    [HttpGet("plans")]
    [ProducesResponseType(typeof(List<PlanView>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetPlans(CancellationToken token) =>
        WithBillingAccess(ViewBilling, token, async _ => Ok(await _views.GetPublicPlansAsync(token)));

    /// <summary>Choose a plan: upgrades wait for payment, downgrades apply at period end.</summary>
    [HttpPost("change")]
    [ProducesResponseType(typeof(PlanChangeQuote), StatusCodes.Status200OK)]
    public Task<IActionResult> RequestChange([FromBody] PlanChangeRequest request, CancellationToken token) =>
        WithBillingAccess(ManageBilling, token, async tenantId =>
        {
            var quote = await _subscriptions.RequestPlanChangeAsync(
                await OwnerOfAsync(tenantId, token), request.PlanCode, request.BillingCycle, ActorId, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok(quote);
        });

    [HttpDelete("change")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> WithdrawChange(CancellationToken token) =>
        WithBillingAccess(ManageBilling, token, async tenantId =>
        {
            await _subscriptions.WithdrawPlanChangeAsync(await OwnerOfAsync(tenantId, token), ActorId, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });

    /// <summary>Cancel at the end of the paid period.</summary>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(CancellationToken token) =>
        WithBillingAccess(ManageBilling, token, async tenantId =>
        {
            await _subscriptions.CancelAsync(await OwnerOfAsync(tenantId, token), ActorId, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });

    [HttpPost("resume")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Resume(CancellationToken token) =>
        WithBillingAccess(ManageBilling, token, async tenantId =>
        {
            await _subscriptions.ResumeAsync(await OwnerOfAsync(tenantId, token), ActorId, token);
            await _subscriptions.CommitChangesAsync(token);
            return Ok();
        });

    private string ActorId => User.GetRequiredUserId().ToString();

    private async Task<Guid> OwnerOfAsync(Guid tenantId, CancellationToken token) =>
        (await _tenants.FindTenantAsync(tenantId, token))?.UserId
        ?? throw new KeyNotFoundException("Company not found.");

    // Checked against the membership itself, not UserMembership.HasPermission: that lets Admin
    // through everything, and billing is deliberately not an Admin right.
    private async Task<IActionResult> WithBillingAccess(string code, CancellationToken token, Func<Guid, Task<IActionResult>> action)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        var member = await _members.GetMemberAsync(User.GetRequiredUserId(), tenantId, token);
        var allowed = member is { Status: "Active" }
            && (member.HasRole(TenantRoles.Owner) || member.EffectivePermissionCodes().Contains(code));
        if (!allowed) return Forbid();

        return await SubscriptionResults.Run(() => action(tenantId));
    }
}

public record PlanChangeRequest
{
    public string PlanCode { get; set; } = string.Empty;
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
}
