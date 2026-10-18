using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Portal;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// Sign-in for the Client Portal's users. Only the portal's server calls these (the browser never talks to AuthApi for the portal), so
/// every action requires its service key and nothing else gets an answer; the end user's address comes in a header so the rate limits
/// count people, not the one server. See <see cref="PortalAuthService"/>.
/// </summary>
[Route("api/v{version:apiVersion}/portal/auth")]
[ApiVersion("1.0")]
[ApiController]
[AllowAnonymous]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
public class PortalAuthController : PortalServiceControllerBase
{
    private readonly PortalAuthService _auth;

    public PortalAuthController(PortalAuthService auth, IOptions<PortalAuthSettings> settings) : base(settings)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.PortalLogin)]
    [ProducesResponseType(typeof(PortalSession), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] PortalLoginRequest request, CancellationToken ct) =>
        Ok(await _auth.LoginAsync(request.Email, request.Password, ct));

    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.PortalRefresh)]
    [ProducesResponseType(typeof(PortalSession), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] PortalRefreshRequest request, CancellationToken ct) =>
        Ok(await _auth.RefreshAsync(request.RefreshToken, ct));

    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicies.PortalRefresh)]
    public async Task<IActionResult> Logout([FromBody] PortalRefreshRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.PortalEmail)]
    public async Task<IActionResult> ForgotPassword([FromBody] PortalForgotPasswordRequest request, CancellationToken ct)
    {
        await _auth.ForgotPasswordAsync(request.Email, ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.PortalPassword)]
    public async Task<IActionResult> ResetPassword([FromBody] PortalResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request.Email, request.Token, request.NewPassword, ct);
        return NoContent();
    }

    /// <summary>Creates the login for a person the portal has verified an invitation for.</summary>
    [HttpPost("accounts")]
    [EnableRateLimiting(RateLimitPolicies.PortalPassword)]
    [ProducesResponseType(typeof(PortalEnsureAccountResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> EnsureAccount([FromBody] PortalEnsureAccountRequest request, CancellationToken ct) =>
        Ok(await _auth.EnsureAccountAsync(request, ct));
}
