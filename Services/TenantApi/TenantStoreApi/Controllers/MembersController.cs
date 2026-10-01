using TenantStoreApi.Core.Filters;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Core.Utilities;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class MembersController : ControllerBase
{
    private readonly UserMembershipService _service;
    private readonly ITenantProvider _tenantProvider;
    private readonly UserInfoService _userInfoService;
    private readonly PlanAllowanceService _allowance;
    public MembersController(UserMembershipService service, ITenantProvider tenantProvider, UserInfoService userInfoService, PlanAllowanceService allowance)
    {
        _service = service;
        _allowance = allowance;
        _tenantProvider = tenantProvider;
        _userInfoService = userInfoService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<MemberResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMembers(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var members = await _service.GetMembersAsync(tenantId, token);

        var activeUserIds = members
            .Where(m => m.UserId != Guid.Empty)
            .Select(m => m.UserId.ToString())
            .Distinct()
            .ToList();

        var result = members.Select(m =>
        {
            var response = new MemberResponse
            {
                Id = m.Id,
                UserId = m.UserId,
                Roles = m.RoleNames(),
                FullName = m.FullName,
                Email = m.InvitedEmail,
                Status = m.Status ?? "Inactive"
            };
            return response;
        }).ToList();
        return Ok(result);
    }

    [HttpGet("account-tenants")]
    [ProducesResponseType(typeof(List<AccountMemberShipQuery>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [AllowWhenLocked]
    public async Task<ActionResult<List<AccountMemberShipQuery>>> FindTenants([FromQuery(Name = "user-id")] Guid? userId)
    {
        // 1. Check if the query parameter was passed (e.g., during login service-to-service calls)
        // 2. If not passed, fallback to the authenticated user context (normal frontend calls)
        var targetUserId = userId ?? User.GetRequiredUserId();
        var data = await _service.GetUserMembersAsync(targetUserId);
        return Ok(data);
    }

    /// <summary>Replaces a member's entire role set with a single role (backward-compatible
    /// single-role update). Use POST/DELETE {userId}/roles to grant/revoke individual roles
    /// while leaving the member's other roles untouched.</summary>
    [HttpPatch("{membershipId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(Guid membershipId, [FromBody] UpdateRoleRequest request, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            // A plan's admin seats: refused before the change, not after.
            await _allowance.EnsureAdminSeatForChangeAsync(tenantId, membershipId, [request.Role], null, token);
            await _service.ReplaceRolesAsync(callerId, membershipId, tenantId, [request.Role], token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (PlanAllowanceException ex) { return SubscriptionResults.Forbidden(ex.Allowance, ex.LimitKey); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Replaces all roles for a member at once.</summary>
    [HttpPut("{membershipId:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplaceRoles(Guid membershipId, [FromBody] ReplaceRolesRequest request, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            // A plan's admin seats: refused before the change, not after.
            await _allowance.EnsureAdminSeatForChangeAsync(tenantId, membershipId, request.Roles, null, token);
            await _service.ReplaceRolesAsync(callerId, membershipId, tenantId, request.Roles, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (PlanAllowanceException ex) { return SubscriptionResults.Forbidden(ex.Allowance, ex.LimitKey); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Updates a member's status (e.g. Active, Revoked, Inactive).</summary>
    [HttpPatch("{membershipId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid membershipId, [FromBody] UpdateStatusRequest request, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            // A plan's admin seats: refused before the change, not after.
            await _allowance.EnsureAdminSeatForChangeAsync(tenantId, membershipId, null, request.Status, token);
            await _service.UpdateStatusAsync(callerId, membershipId, tenantId, request.Status, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (PlanAllowanceException ex) { return SubscriptionResults.Forbidden(ex.Allowance, ex.LimitKey); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Grants an additional role to a member without touching their other roles.</summary>
    [HttpPost("{membershipId:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddRole(Guid membershipId, [FromBody] UpdateRoleRequest request, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            // A plan's admin seats: refused before the change, not after.
            await _allowance.EnsureAdminSeatForChangeAsync(tenantId, membershipId, await RolesWithAsync(membershipId, tenantId, request.Role, token), null, token);
            await _service.AddRoleAsync(callerId, membershipId, tenantId, request.Role, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (PlanAllowanceException ex) { return SubscriptionResults.Forbidden(ex.Allowance, ex.LimitKey); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>Revokes a single role from a member, leaving any other roles intact.</summary>
    [HttpDelete("{membershipId:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(Guid membershipId, string role, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            await _service.RemoveRoleAsync(callerId, membershipId, tenantId, role, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (PlanAllowanceException ex) { return SubscriptionResults.Forbidden(ex.Allowance, ex.LimitKey); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    private async Task<List<string>> RolesWithAsync(Guid membershipId, Guid tenantId, string role, CancellationToken token)
    {
        var member = await _service.GetMemberByIdAsync(membershipId, tenantId, token);
        return [.. member?.RoleNames() ?? [], role];
    }

    [HttpDelete("{membershipId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid membershipId, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var callerId = User.GetRequiredUserId();
        try
        {
            await _service.RemoveMemberAsync(callerId, membershipId, tenantId, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // Leaving a company stays possible while it's locked.
    [AllowWhenLocked]
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

public record ReplaceRolesRequest
{
    public List<string> Roles { get; set; } = new();
}

public record UpdateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
