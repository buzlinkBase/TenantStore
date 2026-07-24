using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;

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
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest payload, CancellationToken token)
        {
            var response = await _service.Create(payload, User, token);
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
        }
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
            Role = response.Role,
            Email = response.Email,
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