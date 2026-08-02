using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
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

        public UsersController(UserService service,
            JwtService jwtService, IOptions<Domains> options)
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
            var accountApiHost = _options.BaseUrl;
            if (string.IsNullOrEmpty(accountApiHost))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Api URL is not configured.");
            }
            var result = await _service.RegisterAccount(payload, accountApiHost, token);

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
        [ProducesResponseType(StatusCodes.Status302Found)] // Redirect returns a 302 Found, not a 200 OK
        public async Task<IActionResult> Confirm([FromQuery(Name = "token")] string token, CancellationToken ct)
        {
            var result = await _service.ConfirmedRegistration(token, ct);
            // 1. Get the configured frontend base URL
            var frontEndHost = _options.FrontEnd;
            if (string.IsNullOrEmpty(frontEndHost))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Frontend URL is not configured.");
            }
            // 2. Normalize the URL by ensuring it doesn't end with a trailing slash 
            // to avoid double slashes like "https://domain.com//account-confirmation"
            var baseUri = frontEndHost.TrimEnd('/');
            // 3. Handle failure with safe URL encoding
            if (!result.Success)
            {
                var errorCode = Uri.EscapeDataString(result.ErrorCode ?? string.Empty);
                var reason = Uri.EscapeDataString(result.Message ?? string.Empty);
                return Redirect($"{baseUri}/account-confirmation/error?code={errorCode}&reason={reason}");
            }
            // 4. Handle success with safe URL encoding
            var email = Uri.EscapeDataString(result.Email ?? string.Empty);
            var name = Uri.EscapeDataString(result.Name ?? string.Empty);
            return Redirect($"{baseUri}/account-confirmation/success?email={email}&name={name}");
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPasswordRequestAsync([FromBody] EmailPayload payload, CancellationToken token)
        {
            var frontEndHost = _options.FrontEnd;
            if (string.IsNullOrEmpty(frontEndHost))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Frontend URL is not configured.");
            }
            await _service.ResetPasswordRequestAsync(payload.Email, frontEndHost, token);
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
                DefaultTenantRoles = response.DefaultTenantRoles,
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
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
        }

        //[HttpGet("login-google")]
        //[AllowAnonymous]
        //public async Task<IActionResult> LoginWithGoogle([FromQuery] string? inviteToken)
        //{
        //    var redirectUrl = Url.Action("GoogleCallback", "users", new { inviteToken }, Request.Scheme);
        //    if (redirectUrl == null)
        //        return BadRequest(new MessageErrorResponse { Message = "Unable to resolve callback URL." });

        //    var properties = await _service.LoginWithGoogleAsync(redirectUrl);
        //    return Challenge(properties, "Google");

        //}
        //[HttpGet("google-callback")]
        //[AllowAnonymous]
        //[ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        //public async Task<IActionResult> GoogleCallback(CancellationToken token)
        //{
        //    var data = await _service.GoogleCallback(token);
        //    if (!string.IsNullOrWhiteSpace(data.ErrorMessage))
        //        return Unauthorized(new UnauthorizedResponse { ErrorMessage = data.ErrorMessage });
        //    return Ok(data);
        //}


        [HttpPost("login-google-callback")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWithGoogle([FromBody] GoogleLoginRequest request, CancellationToken token)
        {
            // exchange request.Code with Google to get user info
            var response = await _service.LoginWithGoogleAsync2(request.Code, token);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = response.ErrorMessage });
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
        }

        [HttpPost("set-default-tenant")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> SetDefault([FromBody] TenantIdPayload payload, CancellationToken ct)
        {
            var token = Request.GetAuthorizationToken();
            if (token == null) return Unauthorized();
            var tenantId = Guid.Parse(payload.TenantId);
            if (tenantId == Guid.Empty) throw new ArgumentException("Invalid tenant Id format");

            var userId = User.GetRequiredUserId();
            var response = await _service.SetDefaultTenant(tenantId, userId, ct);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = response.ErrorMessage });
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));

        }

        [AllowAnonymous]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenPayload payload, CancellationToken ct)
        {
            LoginResponseComposer.SetRefreshTokenCookies("", -1, true, Response);
            if (!string.IsNullOrWhiteSpace(payload.RefreshToken))
                await _service.RevokeRefreshTokenAsync(payload.RefreshToken, ct);
            return Ok();
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Refresh(CancellationToken ct) // Renamed to 'ct' to avoid conflict
        {
            if (!Request.Cookies.TryGetValue("X-Refresh-Token", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
            {
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = "Refresh token is missing or invalid." });
            }
            // 2. Pass the extracted token to your service
            var response = await _service.RefreshLogin(refreshToken, ct);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                return Unauthorized(new UnauthorizedResponse { ErrorMessage = response.ErrorMessage });
            }
            // Optional: If your RefreshLogin method generates a *new* refresh token (rotation), 
            // remember to append the new cookie back to the response here before returning Ok.
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
        }

        [HttpPost("key-gen")]
        [ProducesResponseType(typeof(ApiTokenModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> KeyGen()
        {
            return Ok(_service.KeyGen());
        }
    }
    public class GoogleLoginRequest
    {
        public string Code { get; set; } = string.Empty;
    }

    public record EmailPayload
    {
        public required string Email { get; set; }
    }
    public record TokenPayload
    {
        public required string Token { get; set; }
    }
    public record RefreshTokenPayload
    {
        public required string RefreshToken { get; set; }
    }

    public record TenantIdPayload
    {
        public required string TenantId { get; set; }
    }
}