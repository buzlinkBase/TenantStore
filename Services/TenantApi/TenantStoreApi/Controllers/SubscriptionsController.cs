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
    [ProducesResponseType(typeof(SubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrent(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        var sub = await _subscriptionService.GetCurrentAsync(tenantId, token);
        if (sub == null) return NotFound("No subscription found for this tenant.");

        var isActive = sub.SubStatus == SubscriptionStatus.Active || sub.SubStatus == SubscriptionStatus.Trialing;
        return Ok(new SubscriptionResponse
        {
            Id = sub.Id,
            TenantId = sub.TenantId,
            PlanId = sub.PlanId,
            PlanName = sub.Plan?.Name,
            SubStatus = sub.SubStatus.ToString(),
            StartDate = sub.StartDate,
            EndDate = sub.EndDate,
            DaysRemaining = isActive ? Math.Max(0, (int)(sub.EndDate - DateTime.UtcNow).TotalDays) : 0,
            IsActive = isActive,
        });
    }

    [HttpGet("plans")]
    [ProducesResponseType(typeof(List<PlanResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken token)
    {
        var plans = await _planService.GetAllAsync(token);
        return Ok(plans.Select(p => new PlanResponse
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Days = p.Days,
        }).ToList());
    }
}
