using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using OnePunch.Auth.Api.Extensions;
using OnePunch.Auth.Core.Services;
using System.Security.Claims;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly UserService _service;
        private readonly JwtService _jwtService;
        private readonly Domains _options;

        public UsersController(UserService service, JwtService jwtService, IOptions<Domains> options)
        {
            _service = service;
            _jwtService = jwtService;
            _options = options.Value;
        }

        [HttpPost("create-account")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateAccount([FromBody] CreateAccount payload, CancellationToken token)
        {
            var result = await _service.RegisterAccount(payload, token);
            if (!result.Success)
                return BadRequest(new { result.ErrorCode, result.Message });

            if (Request.Headers["X-Client-Type"] == "WF")
                return Ok(new { result.Email, result.Message });

            return Redirect($"{_options.FrontEnd}/auth/create/success");
        }

        [AllowAnonymous]
        [HttpGet("confirm-email")]
        public async Task<IActionResult> Confirm([FromQuery(Name = "token")] string token, CancellationToken ct)
        {
            var result = await _service.ConfirmedRegistration(token, ct);
            if (!result.Success)
                return Redirect($"{_options.FrontEnd}/tenant/error?code={result.ErrorCode}");
            return Redirect($"{_options.FrontEnd}/tenant/success");
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPasswordRequestAsync([FromQuery] string email, CancellationToken token)
        {
            await _service.ResetPasswordRequestAsync(email, token);
            return Ok();
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPassword payload, CancellationToken token)
        {
            var result = await _service.ResetPassword(payload, token);
            if (!result.Succeeded)
                return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
            return Ok();
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePassword payload, CancellationToken token)
        {
            var result = await _service.ChangePassword(payload, token);
            if (result == null || !result.Succeeded)
                return BadRequest(new { Errors = result?.Errors.Select(e => e.Description) ?? new[] { "Unable to change password." } });
            return Ok();
        }

        [HttpPost("set-password")]
        public async Task<IActionResult> SetPassword([FromBody] SetPassword payload, CancellationToken token)
        {
            if (payload.Password != payload.ConfirmPassword)
                return BadRequest(new { Message = "Passwords do not match." });
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _service.PromoteToPasswordAccount(userId, payload.Password, token);
            if (!result.Succeeded)
                return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
            return Ok();
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile(CancellationToken ct)
        {
            var response = await _service.Profile(HttpContext.User);
            if (response == null) return NotFound();
            return Ok(new
            {
                response.Id,
                response.DefaultTenantName,
                response.DefaultTenantId,
                response.DefaultTenantRole,
                response.Email,
                response.FullName,
                response.PhoneNumber,
                response.Status,
            });
        }

        [HttpPatch("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest payload, CancellationToken ct)
        {
            var userId = User.GetRequiredUserId();
            var result = await _service.UpdateProfileAsync(userId, payload);
            if (!result.Succeeded)
                return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
            return Ok();
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginPayload payload, CancellationToken token)
        {
            var response = await _service.Login(payload, token);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new { response.ErrorMessage });
            return Ok(response);
        }

        [HttpGet("login-google")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWithGoogle([FromQuery] string? inviteToken)
        {
            var redirectUrl = Url.Action("GoogleCallback", "users", new { inviteToken }, Request.Scheme);
            if (redirectUrl == null) return BadRequest("Unable to resolve callback URL.");
            var properties = await _service.LoginWithGoogleAsync(redirectUrl);
            return Challenge(properties, "Google");
        }

        [HttpGet("google-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleCallback(CancellationToken token)
        {
            var data = await _service.GoogleCallback(token);
            if (!string.IsNullOrWhiteSpace(data.ErrorMessage))
                return Unauthorized(new { data.ErrorMessage });
            return Ok(data);
        }

        [HttpPost("set-default-tenant")]
        public async Task<IActionResult> SetDefault([FromQuery(Name = "tenant-id")] Guid tenantId, CancellationToken ct)
        {
            var token = Request.GetAuthorizationToken();
            if (token == null) return Unauthorized();
            var response = await _service.SetDefaultTenant(tenantId, token, ct);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new { response.ErrorMessage });
            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromQuery(Name = "refresh-token")] string? refreshToken, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _service.RevokeRefreshTokenAsync(refreshToken, ct);
            return Ok();
        }

        [AllowAnonymous]
        [HttpGet("refresh")]
        public async Task<IActionResult> Refresh([FromQuery(Name = "refresh-token")] string refreshToken, CancellationToken token)
        {
            var response = await _service.RefreshLogin(refreshToken, token);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new { response.ErrorMessage });
            return Ok(response);
        }

        [HttpPost("key-gen")]
        public async Task<IActionResult> KeyGen()
        {
            return Ok(_service.KeyGen());
        }
    }
}
