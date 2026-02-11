using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Auth.Core.Providers;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Core.Utilities;
using System.Security.Claims;
using System.Text;

namespace OnePunch.Auth.Api.Controllers
{

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {

        private readonly UserService _service;
        private readonly AuthDomainProvider _authDomainProvider;
        private readonly TenantHttpClient _tenantHttpClient;
        private readonly JwtService _jwtService;

        public UserController(UserService service,
            AuthDomainProvider authDomainProvider,
            TenantHttpClient tenantHttpClient,
            JwtService jwtService)
        {
            _service = service;
            _authDomainProvider = authDomainProvider;
            _tenantHttpClient = tenantHttpClient;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterInvitesAsync([FromBody] CreateInvitedUser payload)
        {
            var result = await _service.RegisterInvitesAsync(payload);
            if (result.result.Succeeded)
                return Ok(result);

            return BadRequest(result.result.Errors);
        }

        [HttpPost("send-invite")]
        public async Task<IActionResult> InviteUser([FromQuery] InvitationPayload payload)
        {
            var token = await HttpContext.GetTokenAsync("access_token");
            if (token == null) return Unauthorized();
            var jti = _jwtService.ReadTokenToObject(token);
            if (jti == null) return Unauthorized();
            if (jti.IsExpired) return Unauthorized();

            var tenant = await _tenantHttpClient.GetTenant(jti.TenantId, token);
            if (tenant == null) return BadRequest("Unable to load tenant");

            var result = await _service.SendInvite(payload, tenant);
            if (result)
                return Ok(result);

            return BadRequest("Failed sending invites");

        }

        [AllowAnonymous]
        [HttpGet("check-email")]
        public async Task<ActionResult<bool>> FindByEmail([FromQuery] string email)
        {
            var user = await _service.GetByEmailAsync(email);
            if (user == null) return Ok(false);

            return Ok(user.Status != OutBoxState.FAILED.ToString()
                   && user.Status != OutBoxState.EXPIRED.ToString()
                   && user.Status != OutBoxState.INVALID.ToString());
        }

        [AllowAnonymous]
        [HttpGet("confirm-email")]
        public async Task<IActionResult> Confirm([FromQuery] string token)
        {
            var result = await _service.ConfirmedRegistration(token);
            var url = _authDomainProvider.Resolve().FrontEndDomain ?? "https://default-frontend.com";

            if (!result.Success)
                return Redirect($"{url}/error/{result.ErrorCode}");

            return Redirect($"{url}/success");
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginPayload payload)
        {
            var response = await _service.Login(payload);
            if (!response.Success)
                return Unauthorized(new { response.ErrorMessage });

            return Ok(response);
        }

        [AllowAnonymous]
        [HttpGet("refresh/{refresh}")]
        public async Task<IActionResult> Refresh(string RefreshToken)
        {
            var response = await _service.RefreshLogin(RefreshToken);
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