using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Filters;

namespace TenantStoreApi.Controllers;

/// <summary>
/// The people of one client (client-portal users) and the roles they hold there. The actions and the
/// rules (ClientMembershipService) are the same whoever asks; only the way the caller is identified
/// differs, which each door below supplies: company staff with their HRIS token, or the portal's server
/// acting on behalf of a person it has authenticated.
/// </summary>
public abstract class ClientMembersControllerBase : ControllerBase
{
    private readonly ClientMembershipService _service;

    protected ClientMembersControllerBase(ClientMembershipService service)
    {
        _service = service;
    }

    /// <summary>Who is acting on this client. Throws the usual refusals (Unauthorized, KeyNotFound) when they have no standing.</summary>
    protected abstract Task<ClientCaller> CallerAsync(Guid clientEntityId, CancellationToken token);

    [HttpGet]
    [ProducesResponseType(typeof(List<ClientMemberView>), StatusCodes.Status200OK)]
    public Task<IActionResult> List(Guid clientEntityId, CancellationToken token) =>
        RunAsync(clientEntityId, async caller => Ok(await _service.ListMembersAsync(caller, clientEntityId, token)), token);

    [HttpPut("{membershipId:guid}/roles")]
    public Task<IActionResult> SetRoles(Guid clientEntityId, Guid membershipId, [FromBody] SetClientMemberRolesRequest request, CancellationToken token) =>
        RunAsync(clientEntityId, async caller =>
        {
            await _service.SetRolesAsync(caller, clientEntityId, membershipId, request.RoleIds, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }, token);

    [HttpPatch("{membershipId:guid}/status")]
    public Task<IActionResult> SetStatus(Guid clientEntityId, Guid membershipId, [FromBody] UpdateStatusRequest request, CancellationToken token) =>
        RunAsync(clientEntityId, async caller =>
        {
            await _service.SetStatusAsync(caller, clientEntityId, membershipId, request.Status, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }, token);

    [HttpDelete("{membershipId:guid}")]
    public Task<IActionResult> Remove(Guid clientEntityId, Guid membershipId, CancellationToken token) =>
        RunAsync(clientEntityId, async caller =>
        {
            await _service.RemoveFromClientAsync(caller, clientEntityId, membershipId, token);
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

/// <summary>Company staff who run the portal: an active member of the company that owns the client, holding clientportal:Client:Manage.</summary>
[Route("api/v{version:apiVersion}/clients/{clientEntityId:guid}/members")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class ClientMembersController : ClientMembersControllerBase
{
    private readonly ClientAccessService _access;
    private readonly ITenantProvider _tenantProvider;

    public ClientMembersController(ClientMembershipService service, ClientAccessService access, ITenantProvider tenantProvider) : base(service)
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

/// <summary>
/// Every client user of the company at once (the Users page's Client tab). Company staff who run the portal only; changing one of them
/// goes through the per-client door above.
/// </summary>
[Route("api/v{version:apiVersion}/clients/members")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class CompanyClientMembersController : ControllerBase
{
    private readonly ClientMembershipService _service;
    private readonly ClientAccessService _access;
    private readonly ITenantProvider _tenantProvider;

    public CompanyClientMembersController(ClientMembershipService service, ClientAccessService access, ITenantProvider tenantProvider)
    {
        _service = service;
        _access = access;
        _tenantProvider = tenantProvider;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<CompanyClientMemberView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");
        try
        {
            await _access.AgencyStaffAsync(User.GetRequiredUserId(), tenantId, token);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        return Ok(await _service.ListAllForCompanyAsync(tenantId, token));
    }
}

/// <summary>
/// The client portal's server, acting for a person it has already authenticated (service-to-service: api-key
/// protected, no user token). The person is named in <see cref="ClientCallerHeader.ActingUser"/>; Tenant
/// Service decides what they may do from what they hold in the client.
/// </summary>
[Route("api/v{version:apiVersion}/internal/clients/{clientEntityId:guid}/members")]
[ApiVersion("1.0")]
[AllowAnonymous]
[ApiKeyAuth]
[ApiController]
public class InternalClientMembersController : ClientMembersControllerBase
{
    private readonly ClientAccessService _access;

    public InternalClientMembersController(ClientMembershipService service, ClientAccessService access) : base(service)
    {
        _access = access;
    }

    protected override async Task<ClientCaller> CallerAsync(Guid clientEntityId, CancellationToken token) =>
        await _access.ResolveCallerAsync(ClientCallerHeader.ActingUserOf(Request), await _access.GetEntityAsync(clientEntityId, token), token);
}

/// <summary>The header that names the person the portal's server is acting for.</summary>
public static class ClientCallerHeader
{
    public const string ActingUser = "X-Acting-User-Id";

    public static Guid ActingUserOf(HttpRequest request) =>
        Guid.TryParse(request.Headers[ActingUser].ToString(), out var id) && id != Guid.Empty
            ? id
            : throw new ArgumentException($"The {ActingUser} header is required.");
}

public record SetClientMemberRolesRequest
{
    public List<Guid> RoleIds { get; set; } = new();
}
