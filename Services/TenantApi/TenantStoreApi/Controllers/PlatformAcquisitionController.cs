using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

/// <summary>
/// Where accounts come from and how they progress (signed up, signed in, created a company, trial, paid), for the
/// Platform Console's Acquisition page. Read-only; needs the same permission as reading accounts.
/// </summary>
[Route("api/v{version:apiVersion}/platform")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformAcquisitionController(PlatformAcquisitionService acquisition) : ControllerBase
{
    /// <summary>Time series by origin and the cohort funnel. Dates are Philippine-time days, inclusive; defaults to the last 30 days.</summary>
    [HttpGet("acquisition")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(AcquisitionReport), StatusCodes.Status200OK)]
    public Task<IActionResult> Report(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] AcquisitionBucket bucket = AcquisitionBucket.Day,
        CancellationToken token = default) =>
        SubscriptionResults.Run(async () =>
        {
            var end = to ?? AcquisitionCalculator.LocalDate(DateTime.UtcNow);
            var start = from ?? end.AddDays(-29);
            return Ok(await acquisition.GetReportAsync(start, end, bucket, token));
        });

    /// <summary>One account's trail: how it began and each step since.</summary>
    [HttpGet("accounts/{ownerUserId:guid}/journey")]
    [PlatformAuthorize(PlatformPermissions.AccountsRead)]
    [ProducesResponseType(typeof(AccountJourney), StatusCodes.Status200OK)]
    public Task<IActionResult> Journey(Guid ownerUserId, CancellationToken token) =>
        SubscriptionResults.Run(async () => Ok(await acquisition.GetJourneyAsync(ownerUserId, token)));
}
