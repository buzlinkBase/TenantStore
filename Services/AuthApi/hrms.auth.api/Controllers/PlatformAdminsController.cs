using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Platform;
using OnePunch.Auth.Api.Extensions;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// Operator management for the Platform Admin Console. Requires the <c>platform.admins.manage</c>
/// permission (SuperAdmin), authenticated with the platform scheme only -- a company user's token is
/// rejected before anything else is looked at.
/// </summary>
[Route("api/v{version:apiVersion}/platform/admins")]
[ApiVersion("1.0")]
[ApiController]
[Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme, Policy = PlatformPermissions.AdminsManage)]
[EnableRateLimiting(RateLimitPolicies.PlatformAdmin)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status403Forbidden)]
public class PlatformAdminsController : ControllerBase
{
    private readonly PlatformAdminService _admins;

    public PlatformAdminsController(PlatformAdminService admins) => _admins = admins;

    private Guid ActorId => User.GetRequiredUserId();

    [HttpGet]
    [ProducesResponseType(typeof(List<PlatformAdminView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _admins.ListAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(PlatformAdminCredentials), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformAdminRequest request, CancellationToken ct) =>
        Ok(await _admins.CreateAsync(request, ActorId, ct));

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(PlatformAdminView), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangePlatformAdminRoleRequest request, CancellationToken ct) =>
        Ok(await _admins.ChangeRoleAsync(id, request.Role, ActorId, ct));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(PlatformAdminView), StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        Ok(await _admins.SetActiveAsync(id, false, ActorId, ct));

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(PlatformAdminView), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) =>
        Ok(await _admins.SetActiveAsync(id, true, ActorId, ct));

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(typeof(PlatformAdminCredentials), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct) =>
        Ok(await _admins.ResetPasswordAsync(id, ActorId, ct));

    [HttpPost("{id:guid}/reset-mfa")]
    [ProducesResponseType(typeof(PlatformAdminView), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct) =>
        Ok(await _admins.ResetMfaAsync(id, ActorId, ct));

    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken ct)
    {
        await _admins.RevokeSessionsAsync(id, ActorId, ct);
        return NoContent();
    }
}
