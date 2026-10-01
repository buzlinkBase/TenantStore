using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Filters;

namespace TenantStoreApi.Controllers;

/// <summary>
/// Service-to-service: hrms-api and hrms-adms-api fetch a company's entitlements here to enforce
/// them (they cache the result and drop it on SubscriptionChanged). Api-key protected, not
/// user-authenticated -- background work and device calls have no user token.
/// </summary>
[Route("api/v{version:apiVersion}/internal/subscriptions")]
[ApiVersion("1.0")]
[AllowAnonymous]
[ApiKeyAuth]
[ApiController]
public class InternalSubscriptionsController : ControllerBase
{
    private readonly EntitlementResolverService _resolver;

    public InternalSubscriptionsController(EntitlementResolverService resolver)
    {
        _resolver = resolver;
    }

    [HttpGet("entitlements/{tenantId:guid}")]
    [ProducesResponseType(typeof(EntitlementSnapshot), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEntitlements(Guid tenantId, CancellationToken token) =>
        Ok(await _resolver.ResolveForTenantAsync(tenantId, token));
}
