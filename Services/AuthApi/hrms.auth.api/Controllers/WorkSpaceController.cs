using System.Security.Claims;
using Onepunch.Common.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Security;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public class WorkSpaceController : ControllerBase
    {
        private readonly WorkspaceService _service;
        private readonly JwtService _jwtService;

        public WorkSpaceController(WorkspaceService service,
            JwtService jwtService,
            IOptions<Domains> options)
        {
            _service = service;
            _jwtService = jwtService;
        }

        [HttpPost()]
        [ForbidWhenImpersonating]
        [EnableRateLimiting(RateLimitPolicies.WorkspaceCreate)]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest payload, CancellationToken token)
        {
            if (!CanCreateCompany(User))
                return Forbid();

            var response = await _service.Create(payload, User, token);
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
        }

        // Only the owner of the company the caller is working in may start another one. A token
        // with no role at all is a person with no company yet, who must be able to create their
        // first.
        public static bool CanCreateCompany(ClaimsPrincipal user) =>
            !user.FindAll(ClaimTypes.Role).Any() || user.IsOwner();
    }
}


public class LoginResponseComposer
{
    public static LoginResponseSimple ConvertLoginResponse(LoginResponse response, int RefreshExpiry, bool isHttps, HttpResponse httpResponse)
    {
        SetRefreshTokenCookies(response.RefreshToken, RefreshExpiry, isHttps, httpResponse);
        return new LoginResponseSimple
        {
            AccessToken = response.AccessToken,
            ErrorMessage = response.ErrorMessage,
            Expiry = response.Expiry,
            Tenants = response.Tenants,
            Name = response.Name,
            Roles = response.Roles,
            Email = response.Email,
            Permissions = response.Permissions,
        };
    }

    public static void SetRefreshTokenCookies(string refreshToken, int expiryDays, bool isHttps, HttpResponse httpResponse)
    {
        // Behind reverse proxies, Request.IsHttps is corrected by UseForwardedHeaders.
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            // None is required for cross-site cookies (frontend and API on different origins),
            // and browsers require Secure=true when SameSite=None.
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Secure = isHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(expiryDays)
        };
        httpResponse.Cookies.Append("X-Refresh-Token", refreshToken, cookieOptions);
    }
}