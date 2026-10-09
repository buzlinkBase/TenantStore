using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Platform;
using OnePunch.Auth.Api.Extensions;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// Sign-in for the Platform Admin Console's operators -- a separate identity from tenant users (own
/// store, signing key, audience; see PlatformAuthService). The console's server side is the only
/// caller, so refresh tokens are passed in the body rather than a browser cookie.
/// Flow: login (password) -> mfa/setup (first time only) -> mfa/verify (code) -> session;
/// then refresh / logout / me / change-password.
/// </summary>
[Route("api/v{version:apiVersion}/platform/auth")]
[ApiVersion("1.0")]
[ApiController]
[Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
public class PlatformAuthController : ControllerBase
{
    private readonly PlatformAuthService _auth;

    public PlatformAuthController(PlatformAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(PlatformLoginChallenge), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] PlatformLoginRequest request, CancellationToken ct) =>
        Ok(await _auth.LoginAsync(request.Email, request.Password, ct));

    [AllowAnonymous]
    [HttpPost("mfa/setup")]
    [EnableRateLimiting(RateLimitPolicies.PlatformMfa)]
    [ProducesResponseType(typeof(PlatformMfaSetup), StatusCodes.Status200OK)]
    public async Task<IActionResult> MfaSetup([FromBody] PlatformMfaSetupRequest request, CancellationToken ct) =>
        Ok(await _auth.GetMfaSetupAsync(request.ChallengeToken, ct));

    [AllowAnonymous]
    [HttpPost("mfa/verify")]
    [EnableRateLimiting(RateLimitPolicies.PlatformMfa)]
    [ProducesResponseType(typeof(PlatformSession), StatusCodes.Status200OK)]
    public async Task<IActionResult> MfaVerify([FromBody] PlatformMfaVerifyRequest request, CancellationToken ct) =>
        Ok(await _auth.VerifyMfaAsync(request.ChallengeToken, request.Code, ct));

    [AllowAnonymous]
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType(typeof(PlatformSession), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] PlatformRefreshRequest request, CancellationToken ct) =>
        Ok(await _auth.RefreshAsync(request.RefreshToken, ct));

    [AllowAnonymous]
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Logout([FromBody] PlatformRefreshRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme, Policy = PlatformAuthDefaults.MfaPolicy)]
    [HttpGet("me")]
    [EnableRateLimiting(RateLimitPolicies.PlatformSession)]
    [ProducesResponseType(typeof(PlatformProfile), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await _auth.GetProfileAsync(User.GetRequiredUserId(), ct));

    [Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme, Policy = PlatformAuthDefaults.MfaPolicy)]
    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitPolicies.PasswordChange)]
    [ProducesResponseType(typeof(PlatformSession), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] PlatformChangePasswordRequest request, CancellationToken ct) =>
        Ok(await _auth.ChangePasswordAsync(User.GetRequiredUserId(), request, ct));
}
