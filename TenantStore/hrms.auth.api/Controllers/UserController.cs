using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using OnePunch.Auth.Core.Services;

namespace OnePunch.Auth.Api.Controllers
{

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly UserService _service;
        private readonly Domains _options;
        public UsersController(UserService service, IOptions<Domains> options)
        {
            _service = service;
            _options = options.Value;
        }
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPassword payload, CancellationToken token)
        {
            var result = await _service.ResetPassword(payload, token);
            var url = _options.FrontEndDomain;
            if (!result.Succeeded)
            {
                return Redirect($"{url}/auth/change-password/error?description={result.Errors.FirstOrDefault()?.Description ?? "error"}");
            }
            return Redirect($"{url}/auth/change-password/success");
        }
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPasswordRequestAsync([FromQuery] string email, CancellationToken token)
        {
            await _service.ResetPasswordRequestAsync(email, token);
            var url = _options.FrontEndDomain;
            return Redirect($"{url}/forgot-password/success");
        }
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePassword email, CancellationToken token)
        {
            await _service.ChangePassword(email, token);
            var url = _options.FrontEndDomain;
            return Redirect($"{url}/forgot-password/success");
        }

        [AllowAnonymous]
        [HttpGet("confirm-email")]
        public async Task<IActionResult> Confirm([FromQuery(Name = "token")] string token, CancellationToken ct)
        {
            var result = await _service.ConfirmedRegistration(token, ct);
            var url = _options.FrontEndDomain;

            if (!result.Success)
                return Redirect($"{url}/tenant/error?code={result.ErrorCode}");

            return Redirect($"{url}/tenant/success");
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginPayload payload, CancellationToken token)
        {
            var response = await _service.Login(payload, token);
            if (!response.Success)
                return Unauthorized(new { response.ErrorMessage });

            return Ok(response);
        }

        [AllowAnonymous]
        [HttpGet("refresh/{refresh}")]
        public async Task<IActionResult> Refresh(string RefreshToken, CancellationToken token)
        {
            var response = await _service.RefreshLogin(RefreshToken, token);
            if (!response.Success)
                return Unauthorized(new { response.ErrorMessage });

            return Ok(response);
        }

        //[HttpPost("key-gen")]
        //public async Task<IActionResult> KeyGen()
        //{
        //    var response = _service.KeyGen();
        //    return Ok(response);
        //}
    }
}