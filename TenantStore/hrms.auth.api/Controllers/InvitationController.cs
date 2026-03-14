using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class InvitationController : ControllerBase
{
    private readonly InvitationService _service;
    public InvitationController(InvitationService service)
    {
        _service = service;
    }

    [HttpPost("send-invite")]
    public async Task<IActionResult> InviteUser([FromQuery] InvitationRequest payload, CancellationToken token)
    {
        try
        {
            await _service.SendUserInvitationAsync(payload, token);
            return Ok();
        }
        catch (UnauthorizedException ex)
        {
            return Unauthorized();
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromQuery] string invitationToken,CancellationToken token)
    {
        try
        {
            await _service.Accept(invitationToken, token);
            return Ok();
        }
        catch (UnauthorizedException){
            return Unauthorized();  
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    [HttpGet("check-invitation")]
    [AllowAnonymous]
    public async Task<IActionResult> IsInvitationValid([FromQuery(Name = "invitation-token")] string invitationToken, CancellationToken token)
    {
        return Ok(await _service.IsValidAsync(invitationToken, token));
    }
}
