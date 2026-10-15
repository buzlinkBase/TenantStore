using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Portal;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// Invitations of client-portal users. Only the portal's server calls these (service key, see
/// <see cref="PortalServiceControllerBase"/>); it has already authenticated the person acting and passes
/// their id, which Tenant Service checks against what they hold in the client. See <see cref="PortalInvitationService"/>.
/// </summary>
[Route("api/v{version:apiVersion}/portal/invitations")]
[ApiVersion("1.0")]
[ApiController]
[AllowAnonymous]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
public class PortalInvitationsController : PortalServiceControllerBase
{
    private readonly PortalInvitationService _invitations;

    public PortalInvitationsController(PortalInvitationService invitations, IOptions<PortalAuthSettings> settings) : base(settings)
    {
        _invitations = invitations;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.PortalEmail)]
    [ProducesResponseType(typeof(PortalInvitationView), StatusCodes.Status200OK)]
    public async Task<IActionResult> Invite([FromBody] PortalInviteRequest request, CancellationToken ct) =>
        Ok(await _invitations.InviteAsync(request, ct));

    [HttpGet]
    [ProducesResponseType(typeof(List<PortalInvitationView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPending([FromQuery] Guid clientEntityId, CancellationToken ct) =>
        Ok(await _invitations.ListPendingAsync(clientEntityId, ct));

    [HttpPost("{id:guid}/resend")]
    [EnableRateLimiting(RateLimitPolicies.PortalEmail)]
    [ProducesResponseType(typeof(PortalInvitationView), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(Guid id, [FromBody] PortalInvitationActionRequest request, CancellationToken ct) =>
        Ok(await _invitations.ResendAsync(id, request.ActingUserId, ct));

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, [FromBody] PortalInvitationActionRequest request, CancellationToken ct)
    {
        await _invitations.RevokeAsync(id, request.ActingUserId, ct);
        return NoContent();
    }

    /// <summary>What the accept page shows for a link. 404 when the link is unknown.</summary>
    [HttpGet("preview")]
    [EnableRateLimiting(RateLimitPolicies.PortalPassword)]
    [ProducesResponseType(typeof(PortalInvitationPreview), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview([FromQuery] string? token, CancellationToken ct)
    {
        var preview = await _invitations.PreviewAsync(token, ct);
        return preview is null ? NotFound() : Ok(preview);
    }

    [HttpPost("accept")]
    [EnableRateLimiting(RateLimitPolicies.PortalPassword)]
    [ProducesResponseType(typeof(PortalAcceptInvitationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept([FromBody] PortalAcceptInvitationRequest request, CancellationToken ct) =>
        Ok(await _invitations.AcceptAsync(request, ct));
}
