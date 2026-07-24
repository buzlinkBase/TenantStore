using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class MembersController : ControllerBase
{
    private readonly UserMembershipService _service;
    private readonly ITenantProvider _tenantProvider;
    public MembersController(UserMembershipService service, ITenantProvider tenantProvider)
    {
        _service = service;
        _tenantProvider = tenantProvider;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<MemberResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMembers(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var members = await _service.GetMembersAsync(tenantId, token);
        return Ok(members.Select(m => new MemberResponse { UserId = m.UserId, Role = m.Role }).ToList());
    }

    [HttpGet("account-tenants")]
    [ProducesResponseType(typeof(List<AccountMemberShipQuery>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AccountMemberShipQuery>>> FindTenants([FromQuery(Name = "user-id")] Guid? userId)
    {
        // 1. Check if the query parameter was passed (e.g., during login service-to-service calls)
        // 2. If not passed, fallback to the authenticated user context (normal frontend calls)
        var targetUserId = userId ?? User.GetRequiredUserId();
        var data = await _service.GetUserMembersAsync(targetUserId);
        return Ok(data);
    }

    [HttpPatch("{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(Guid userId, [FromBody] UpdateRoleRequest request, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            await _service.UpdateRoleAsync(callerId, userId, tenantId, request.Role, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid userId, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        var callerId = User.GetRequiredUserId();
        try
        {
            await _service.RemoveMemberAsync(callerId, userId, tenantId, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LeaveTenant(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        var userId = User.GetRequiredUserId();
        try
        {
            await _service.LeaveTenantAsync(userId, tenantId, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}

public record UpdateRoleRequest
{
    public string Role { get; set; } = string.Empty;
}
