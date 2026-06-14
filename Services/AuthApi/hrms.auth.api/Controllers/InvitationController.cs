using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using System.Security.Claims;

namespace OnePunch.Auth.Api.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiController]
[Authorize]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> InviteUser([FromBody] InvitationRequest payload, CancellationToken ct)
    {
        try
        {
            var token = HttpContext.Request.GetAuthorizationToken();
            if (token == null) return Unauthorized();
            var tokenInfo = _jwtService.ReadTokenToObject(token);
            if (tokenInfo == null) return Unauthorized();
            await _service.SendUserInvitationAsync(payload, tokenInfo.TenantId, tokenInfo.TenantName, User, ct);
            return NoContent();
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)] 
    public async Task<IActionResult> Accept([FromQuery] string invitationToken, CancellationToken token)
    {
        try
        {
            await _service.Accept(invitationToken, User, token);
            return NoContent();
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
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> IsInvitationValid([FromQuery(Name = "invitation-token")] string invitationToken, CancellationToken token)
    {
        var result = await _service.IsValidAsync(invitationToken, token);
        return Ok(result);
    }

    [HttpGet("my-invitations")]
    [ProducesResponseType(typeof(List<Invitation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyInvitations(CancellationToken token)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email)) return Unauthorized();
        var invitations = await _service.GetPendingByEmailAsync(email, token);
        return Ok(invitations);
    }
}
