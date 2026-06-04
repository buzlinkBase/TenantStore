using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class SubscriptionsController : ControllerBase
{
    private readonly SubscriptionService _subscriptionService;
    private readonly PlanService _planService;
    private readonly ITenantProvider _tenantProvider;

    public SubscriptionsController(
        SubscriptionService subscriptionService,
        PlanService planService,
        ITenantProvider tenantProvider)
    {
        _subscriptionService = subscriptionService;
        _planService = planService;
        _tenantProvider = tenantProvider;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        var sub = await _subscriptionService.GetCurrentAsync(tenantId, token);
        if (sub == null) return NotFound("No subscription found for this tenant.");

        return Ok(new
        {
            sub.Id,
            sub.TenantId,
            sub.PlanId,
            PlanName = sub.Plan?.Name,
            sub.SubStatus,
            sub.StartDate,
            sub.EndDate,
            DaysRemaining = sub.SubStatus == SubscriptionStatus.Active || sub.SubStatus == SubscriptionStatus.Trialing
                ? Math.Max(0, (int)(sub.EndDate - DateTime.UtcNow).TotalDays)
                : 0,
            IsActive = sub.SubStatus == SubscriptionStatus.Active || sub.SubStatus == SubscriptionStatus.Trialing,
        });
    }

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken token)
    {
        var plans = await _planService.GetAllAsync(token);
        return Ok(plans.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.Days,
        }));
    }
}
