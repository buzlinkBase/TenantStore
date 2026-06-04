using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using System.Security.Claims;

namespace OnePunch.Auth.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class InvitationController : ControllerBase
{
    private readonly InvitationService _service;
    private readonly JwtService _jwtService;

    public InvitationController(InvitationService service, JwtService jwtService)
    {
        _service = service;
        _jwtService = jwtService;
    }

    [HttpPost("send-invite")]
    public async Task<IActionResult> InviteUser([FromBody] InvitationRequest payload, CancellationToken ct)
    {
        try
        {
            var token = HttpContext.Request.GetAuthorizationToken();
            if (token == null) return Unauthorized();
            var tokenInfo = _jwtService.ReadTokenToObject(token);
            if (tokenInfo == null) return Unauthorized();
            await _service.SendUserInvitationAsync(payload, tokenInfo.TenantId, tokenInfo.TenantName, User, ct);
            return Ok();
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromQuery] string invitationToken, CancellationToken token)
    {
        try
        {
            await _service.Accept(invitationToken, User, token);
            return Ok();
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("check-invitation")]
    [AllowAnonymous]
    public async Task<IActionResult> IsInvitationValid([FromQuery(Name = "invitation-token")] string invitationToken, CancellationToken token)
    {
        return Ok(await _service.IsValidAsync(invitationToken, token));
    }

    [HttpGet("my-invitations")]
    public async Task<IActionResult> MyInvitations(CancellationToken token)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email)) return Unauthorized();
        var invitations = await _service.GetPendingByEmailAsync(email, token);
        return Ok(invitations);
    }
}
