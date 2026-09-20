using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
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
    private readonly Domains _options;
    private readonly JwtService _jwtService;

    public InvitationController(InvitationService service,
        IOptions<Domains> options,
        JwtService jwtService)
    {
        _service = service;
        _options = options.Value;
        _jwtService = jwtService;
    }

    [HttpPost("send-invite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> InviteUser([FromBody] InvitationRequest payload, CancellationToken ct)
    {
        try
        {
            var accountApiHost = _options.BaseUrl;
            if (string.IsNullOrEmpty(accountApiHost))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Api URL env is not configured.");
            }
            var token = HttpContext.Request.GetAuthorizationToken();
            if (token == null) return Unauthorized();
            var tokenInfo = _jwtService.ReadTokenToObject(token);
            if (tokenInfo == null) return Unauthorized();
            await _service.SendUserInvitationAsync(payload, tokenInfo.TenantId, tokenInfo.TenantName, accountApiHost, User, ct);
            return NoContent();
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("accept")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest payload, CancellationToken ct)
    {
        try
        {
            var response = await _service.Accept(payload.Token, User, ct);
            return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
        }
        catch (UnauthorizedException)
        {
            return Unauthorized();
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

    [HttpGet("preview")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(InvitationPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromQuery] string token, CancellationToken ct)
    {
        var preview = await _service.GetPreviewAsync(token, ct);
        if (preview == null) return NotFound();
        return Ok(preview);
    }

    [HttpPost("accept-by-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptByToken([FromBody] AcceptInvitationByTokenRequest payload, CancellationToken ct)
    {
        var response = await _service.AcceptByTokenAsync(payload.Token, payload.Name, payload.Password, ct);
        return Ok(LoginResponseComposer.ConvertLoginResponse(response, _jwtService.RefreshExpiry, HttpContext.Request.IsHttps, Response));
    }

    [HttpGet("my-invitations")]
    [ProducesResponseType(typeof(List<Invitation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MyInvitations(CancellationToken token)
    {
        var email = User.FindFirstValue("email");
        if (string.IsNullOrEmpty(email)) return Unauthorized();
        var invitations = await _service.GetPendingByEmailAsync(email, token);
        return Ok(invitations);
    }
}
