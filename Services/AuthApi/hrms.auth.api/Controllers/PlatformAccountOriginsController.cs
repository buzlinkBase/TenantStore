using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Auth.Core.Origins;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Platform;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// One-off, operator-run: works out how accounts that pre-date origin tracking began (see
/// <see cref="AccountOriginBackfillService"/>). Idempotent. SuperAdmin only, platform scheme only.
/// </summary>
[Route("api/v{version:apiVersion}/platform/users/origins")]
[ApiVersion("1.0")]
[ApiController]
[Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme, Policy = PlatformPermissions.AdminsManage)]
[EnableRateLimiting(RateLimitPolicies.PlatformAdmin)]
public class PlatformAccountOriginsController(AccountOriginBackfillService backfill) : ControllerBase
{
    [HttpPost("backfill")]
    [ProducesResponseType(typeof(AccountOriginBackfillReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> Backfill(CancellationToken ct) => Ok(await backfill.RunAsync(ct));
}
