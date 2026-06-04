using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services;

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
    public async Task<IActionResult> GetMembers(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        var members = await _service.GetMembersAsync(tenantId, token);
        return Ok(members.Select(m => new { m.UserId, m.Role }));
    }

    [HttpPatch("{userId:guid}/role")]
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
