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
        private readonly JwtService _jwtService;

        public UsersController(UserService service,
            IOptions<Domains> options,
            JwtService jwtService)
        {
            _service = service;
            _options = options.Value;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] CreateInvitedUser payload,CancellationToken token)
        {
            var result = await _service.RegisterInvitesAsync(payload,token);
            var url = _options.FrontEndDomain ?? "https://onepunch.com";
            if (!result.result.Succeeded)
            {
                // Aggregate all Identity errors into one string
                var errorMessages = string.Join(", ", result.result.Errors.Select(e => e.Description));
                throw new Exception($"Failed to create user: {errorMessages}");
            }
            //return Redirect($"{url}/error/{result.result.Errors.FirstOrDefault()?.Code ?? "0000"}");
            return Redirect($"{url}/success");

        }

        [HttpPost("send-invite")]
        public async Task<IActionResult> InviteUser([FromQuery] InvitationPayload payload, CancellationToken token)
        {
            var result = await _service.SendInvite(payload,  token);
            if (result)
                return Ok(result);

            return BadRequest("Failed sending invites");
        }

        //[AllowAnonymous]
        //[HttpGet("check-email")]
        //public async Task<ActionResult<bool>> FindByEmail([FromQuery] string email)
        //{
        //    var user = await _service.GetByEmailAsync(email);
        //    if (user == null) return Ok(false);

        //    return Ok(user.Status != OutBoxState.FAILED.ToString()
        //           && user.Status != OutBoxState.EXPIRED.ToString()
        //           && user.Status != OutBoxState.INVALID.ToString());
        //}

        [AllowAnonymous]
        [HttpGet("confirm-email")]
        public async Task<IActionResult> Confirm([FromQuery(Name = "token")] string token, CancellationToken ct )
        {
            var result = await _service.ConfirmedRegistration(token, ct);
            var url = _options.FrontEndDomain ?? "https://onepunch.com";

            if (!result.Success)
                return Redirect($"{url}/error/{result.ErrorCode}");

            return Redirect($"{url}/success");
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