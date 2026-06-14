using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using OnePunch.Auth.Api.Extensions;
using OnePunch.Auth.Core.Services;
using System.Security.Claims;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]

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
        [ProducesResponseType(typeof(CreateAccountResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateAccount([FromBody] CreateAccount payload, CancellationToken token)
        {
            var result = await _service.RegisterAccount(payload, token);

            if (!result.Success)
            {
                return BadRequest(new CreateAccountErrorResponse
                {
                    ErrorCode = result.ErrorCode,
                    Message = result.Message
                });
            }

            return Ok(new CreateAccountResponse
            {
                Email = result.Email,
                Message = result.Message
            });
        }

        [AllowAnonymous]
        [HttpGet("confirm-email")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Confirm([FromQuery(Name = "token")] string token, CancellationToken ct)
        {
            var result = await _service.ConfirmedRegistration(token, ct);
            if (!result.Success)
                return BadRequest(new MessageErrorResponse { Message = result.ErrorCode });
            return Ok();
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPasswordRequestAsync([FromQuery] string email, CancellationToken token)
        {
            await _service.ResetPasswordRequestAsync(email, token);
            return Ok();
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPassword payload, CancellationToken token)
        {
            var result = await _service.ResetPassword(payload, token);
            if (!result.Succeeded)
                return BadRequest(new ErrorResponse { Errors = result.Errors.Select(e => e.Description) });
            return Ok();
        }

        [HttpPost("change-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePassword payload, CancellationToken token)
        {
            var result = await _service.ChangePassword(payload, token);
            if (result == null || !result.Succeeded)
                return BadRequest(new ErrorResponse { Errors = result?.Errors.Select(e => e.Description) ?? ["Unable to change password."] });
            return Ok();
        }

        [HttpPost("set-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SetPassword([FromBody] SetPassword payload, CancellationToken token)
        {
            if (payload.Password != payload.ConfirmPassword)
                return BadRequest(new ErrorResponse { Errors = ["Passwords do not match."] });
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _service.PromoteToPasswordAccount(userId, payload.Password, token);
            if (!result.Succeeded)
                return BadRequest(new ErrorResponse { Errors = result.Errors.Select(e => e.Description) });
            return NoContent();
        }

        [HttpGet("profile")]
        [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProfile(CancellationToken ct)
        {
            var response = await _service.Profile(HttpContext.User);
            if (response == null) return NotFound();
            return Ok(new UserProfileResponse
            {
                Id = response.Id,
                DefaultTenantName = response.DefaultTenantName,
                DefaultTenantId = response.DefaultTenantId,
                DefaultTenantRole = response.DefaultTenantRole,
                Email = response.Email,
                FullName = response.FullName,
                PhoneNumber = response.PhoneNumber,
                Status = response.Status,
            });
        }

        [HttpPatch("profile")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest payload, CancellationToken ct)
        {
            var user = User;
            var userId = User.GetRequiredUserId();
            var result = await _service.UpdateProfileAsync(userId, payload);
            if (!result.Succeeded)
                return BadRequest(new ErrorResponse { Errors = result.Errors.Select(e => e.Description) });
            return Ok();
        }

        [AllowAnonymous]
        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Login([FromBody] LoginPayload payload, CancellationToken token)
        {
            var response = await _service.Login(payload, token);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = response.ErrorMessage });
            return Ok(response);
        }

        [HttpGet("login-google")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWithGoogle([FromQuery] string? inviteToken)
        {
            var redirectUrl = Url.Action("GoogleCallback", "users", new { inviteToken }, Request.Scheme);
            if (redirectUrl == null)
                return BadRequest(new MessageErrorResponse { Message = "Unable to resolve callback URL." });

            var properties = await _service.LoginWithGoogleAsync(redirectUrl);
            return Challenge(properties, "Google");

        }

        [HttpGet("google-callback")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GoogleCallback(CancellationToken token)
        {
            var data = await _service.GoogleCallback(token);
            if (!string.IsNullOrWhiteSpace(data.ErrorMessage))
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = data.ErrorMessage });
            return Ok(data);
        }

        [HttpPost("set-default-tenant")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> SetDefault([FromQuery(Name = "tenant-id")] Guid tenantId, CancellationToken ct)
        {
            var token = Request.GetAuthorizationToken();
            if (token == null) return Unauthorized();
            var response = await _service.SetDefaultTenant(tenantId, token, ct);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = response.ErrorMessage });
            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout([FromQuery(Name = "refresh-token")] string? refreshToken, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _service.RevokeRefreshTokenAsync(refreshToken, ct);
            return Ok();
        }

        [AllowAnonymous]
        [HttpGet("refresh")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Refresh([FromQuery(Name = "refresh-token")] string refreshToken, CancellationToken token)
        {
            var response = await _service.RefreshLogin(refreshToken, token);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = response.ErrorMessage });
            return Ok(response);
        }

        [HttpPost("key-gen")]
        [ProducesResponseType(typeof(ApiTokenModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> KeyGen()
        {
            return Ok(_service.KeyGen());
        }
    }
}