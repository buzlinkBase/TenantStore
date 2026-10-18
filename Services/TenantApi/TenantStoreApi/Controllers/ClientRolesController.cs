using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Filters;

namespace TenantStoreApi.Controllers;

/// <summary>
/// The roles one client's people can hold -- the built-in client roles plus custom roles the client
/// defines. Same shape as <see cref="ClientMembersControllerBase"/>: shared actions and rules
/// (ClientRoleService), one door per kind of caller.
/// </summary>
public abstract class ClientRolesControllerBase : ControllerBase
{
    private readonly ClientRoleService _service;

    protected ClientRolesControllerBase(ClientRoleService service)
    {
        _service = service;
    }

    protected abstract Task<ClientCaller> CallerAsync(Guid clientEntityId, CancellationToken token);

    [HttpGet]
    [ProducesResponseType(typeof(List<ClientRoleView>), StatusCodes.Status200OK)]
    public Task<IActionResult> List(Guid clientEntityId, CancellationToken token) =>
        RunAsync(clientEntityId, async caller => Ok(await _service.ListRolesAsync(caller, clientEntityId, token)), token);

    /// <summary>The client-side permissions a role can carry.</summary>
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(List<ClientPermissionView>), StatusCodes.Status200OK)]
    public Task<IActionResult> Permissions(Guid clientEntityId, CancellationToken token) =>
        RunAsync(clientEntityId, async _ => Ok(await _service.ListPermissionsAsync(token)), token);

    [HttpPost]
    public Task<IActionResult> Create(Guid clientEntityId, [FromBody] SaveClientRoleRequest request, CancellationToken token) =>
        RunAsync(clientEntityId, async caller =>
        {
            var role = await _service.CreateCustomRoleAsync(caller, clientEntityId, request.Name, request.Description, request.PermissionIds, token);
            await _service.CommitChangesAsync(token);
            return Ok(new { role.Id, role.Name, role.Description });
        }, token);

    [HttpPut("{roleId:guid}")]
    public Task<IActionResult> Update(Guid clientEntityId, Guid roleId, [FromBody] SaveClientRoleRequest request, CancellationToken token) =>
        RunAsync(clientEntityId, async caller =>
        {
            await _service.UpdateCustomRoleAsync(caller, clientEntityId, roleId, request.Name, request.Description, token);
            await _service.SetPermissionsAsync(caller, clientEntityId, roleId, request.PermissionIds, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }, token);

    [HttpDelete("{roleId:guid}")]
    public Task<IActionResult> Delete(Guid clientEntityId, Guid roleId, CancellationToken token) =>
        RunAsync(clientEntityId, async caller =>
        {
            await _service.DeleteCustomRoleAsync(caller, clientEntityId, roleId, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }, token);

    private async Task<IActionResult> RunAsync(Guid clientEntityId, Func<ClientCaller, Task<IActionResult>> action, CancellationToken token)
    {
        try
        {
            return await action(await CallerAsync(clientEntityId, token));
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}

/// <summary>Company staff who run the portal (see <see cref="ClientMembersController"/>).</summary>
[Route("api/v{version:apiVersion}/clients/{clientEntityId:guid}/roles")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class ClientRolesController : ClientRolesControllerBase
{
    private readonly ClientAccessService _access;
    private readonly ITenantProvider _tenantProvider;

    public ClientRolesController(ClientRoleService service, ClientAccessService access, ITenantProvider tenantProvider) : base(service)
    {
        _access = access;
        _tenantProvider = tenantProvider;
    }

    protected override Task<ClientCaller> CallerAsync(Guid clientEntityId, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant context is required.");
        return _access.AgencyCallerAsync(User.GetRequiredUserId(), tenantId, clientEntityId, token);
    }
}

/// <summary>The client portal's server acting for a person it has authenticated (see <see cref="InternalClientMembersController"/>).</summary>
[Route("api/v{version:apiVersion}/internal/clients/{clientEntityId:guid}/roles")]
[ApiVersion("1.0")]
[AllowAnonymous]
[ApiKeyAuth]
[ApiController]
public class InternalClientRolesController : ClientRolesControllerBase
{
    private readonly ClientAccessService _access;

    public InternalClientRolesController(ClientRoleService service, ClientAccessService access) : base(service)
    {
        _access = access;
    }

    protected override async Task<ClientCaller> CallerAsync(Guid clientEntityId, CancellationToken token) =>
        await _access.ResolveCallerAsync(ClientCallerHeader.ActingUserOf(Request), await _access.GetEntityAsync(clientEntityId, token), token);
}

public record SaveClientRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Guid> PermissionIds { get; set; } = new();
}
