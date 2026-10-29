using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Security;
using OnePunch.Auth.Api.Extensions;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// The platform side of impersonation: an operator asks to sign in as a company user. Platform scheme
/// only, so a company user's token is rejected before anything else is looked at.
/// </summary>
[Route("api/v{version:apiVersion}/platform/impersonation")]
[ApiVersion("1.0")]
[ApiController]
[Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme, Policy = PlatformPermissions.ImpersonateUsers)]
[EnableRateLimiting(RateLimitPolicies.PlatformAdmin)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status403Forbidden)]
public class PlatformImpersonationController(PlatformImpersonationService impersonation) : ControllerBase
{
    /// <summary>Records the session and returns a one-time code for the HRIS app to redeem.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(StartImpersonationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start([FromBody] StartImpersonationRequest payload, CancellationToken ct) =>
        Ok(await impersonation.StartAsync(User.GetRequiredUserId(), payload, ct));
}

/// <summary>
/// The HRIS side: redeem the code for the impersonated session, and end it. The exchange is anonymous
/// (the code is the credential) and rate limited like a login; Stop needs the impersonation token itself.
/// </summary>
[Route("api/v{version:apiVersion}/auth/impersonation")]
[ApiVersion("1.0")]
[ApiController]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
public class ImpersonationController(PlatformImpersonationService impersonation) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("exchange")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(ImpersonationSessionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Exchange([FromBody] ExchangeImpersonationRequest payload, CancellationToken ct) =>
        Ok(await impersonation.ExchangeAsync(payload.Code, ct));

    [HttpPost("stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        var sessionId = User.GetImpersonationSessionId();
        if (sessionId == null) return BadRequest("This is not an impersonation session.");
        await impersonation.StopAsync(sessionId.Value, ct);
        return NoContent();
    }
}
