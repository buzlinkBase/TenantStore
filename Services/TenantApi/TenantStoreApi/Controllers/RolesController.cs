using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class RolesController : ControllerBase
{
    private readonly RoleService _service;
    private readonly UserMembershipService _membershipService;
    private readonly ITenantProvider _tenantProvider;

    public RolesController(RoleService service, UserMembershipService membershipService, ITenantProvider tenantProvider)
    {
        _service = service;
        _membershipService = membershipService;
        _tenantProvider = tenantProvider;
    }

    // Managing Custom Roles requires Tenant Roles:Manage in the current tenant (the existing,
    // coarse check every Admin already holds) -- checked here rather than inside RoleService to
    // avoid a circular dependency (UserMembershipService already depends on RoleService to
    // resolve role names). additionalCode is the finer-grained Roles:{Action} code (from the
    // catalog's "Security" module -- codes are just "{Feature}:{Action}", the module name isn't
    // part of the code itself) for this specific action, any-of with the existing check -- lets
    // a tenant grant just this one action to a Custom Role without needing the old blanket
    // Manage permission.
    private async Task<bool> CallerCanManageRolesAsync(Guid tenantId, string additionalCode, CancellationToken token)
    {
        var caller = await _membershipService.GetMemberAsync(User.GetRequiredUserId(), tenantId, token);
        return caller != null && caller.HasAnyPermission("Tenant Roles:Manage", additionalCode);
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        var data = await _service.FindAllForTenantAsync(tenantId, token);
        return Ok(data);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken token)
    {
        var data = await _service.FindOneWithPermissionsAsync(id, token);
        if (data == null) return NotFound();
        return Ok(data);
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CreateRoleRequest payload, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        if (!await CallerCanManageRolesAsync(tenantId, "Roles:Create", token)) return Forbid();

        var role = await _service.AddCustomRoleAsync(tenantId, payload.Name, payload.Description, token);
        return Ok(role);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Put(Guid id, [FromBody] UpdateRoleDetailsRequest payload, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        if (!await CallerCanManageRolesAsync(tenantId, "Roles:Edit", token)) return Forbid();

        try
        {
            var role = await _service.UpdateCustomRoleAsync(id, payload.Name, payload.Description, token);
            return Ok(role);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        if (!await CallerCanManageRolesAsync(tenantId, "Roles:Delete", token)) return Forbid();

        try
        {
            await _service.DeleteCustomRoleAsync(id, token);
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] SetRolePermissionsRequest payload, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        if (!await CallerCanManageRolesAsync(tenantId, "Roles:Manage", token)) return Forbid();

        try
        {
            await _service.SetPermissionsAsync(id, payload.PermissionIds, token);
            return Ok();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}

public record CreateRoleRequest { public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; }
public record UpdateRoleDetailsRequest { public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; }
public record SetRolePermissionsRequest { public List<Guid> PermissionIds { get; set; } = new(); }
