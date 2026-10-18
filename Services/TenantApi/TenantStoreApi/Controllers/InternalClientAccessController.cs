using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Filters;

namespace TenantStoreApi.Controllers;

/// <summary>
/// Service-to-service for the client portal's server: what a signed-in person may do within a client,
/// which clients they belong to, and the portal telling us about a client it created or changed.
/// Api-key protected, not user-authenticated -- the portal has already authenticated the person.
/// (Changing members and roles is <see cref="InternalClientMembersController"/> and
/// <see cref="InternalClientRolesController"/>.)
/// </summary>
[Route("api/v{version:apiVersion}/internal/client-access")]
[ApiVersion("1.0")]
[AllowAnonymous]
[ApiKeyAuth]
[ApiController]
public class InternalClientAccessController : ControllerBase
{
    private readonly ClientAccessService _access;
    private readonly ClientEntityService _entities;
    private readonly ClientAccessImportService _import;

    public InternalClientAccessController(ClientAccessService access, ClientEntityService entities, ClientAccessImportService import)
    {
        _access = access;
        _entities = entities;
        _import = import;
    }

    /// <summary>
    /// Brings a client's existing people and custom roles over from the portal's own tables (the one-time move). Idempotent and additive:
    /// run it again and nothing the first run did changes. What could not be brought over comes back as warnings.
    /// </summary>
    [HttpPut("clients/{clientEntityId:guid}/import")]
    [ProducesResponseType(typeof(ImportReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(Guid clientEntityId, [FromBody] ClientAccessImport request, CancellationToken token)
    {
        try
        {
            var report = await _import.ImportAsync(clientEntityId, request, token);
            await _import.CommitChangesAsync(token);
            return Ok(report);
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>What this person may do within one client. 404 when they have no standing there.</summary>
    [HttpGet("accounts/{accountId:guid}/clients/{clientEntityId:guid}")]
    [ProducesResponseType(typeof(ClientAccess), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(Guid accountId, Guid clientEntityId, CancellationToken token)
    {
        var access = await _access.ResolveAsync(accountId, clientEntityId, token);
        return access == null ? NotFound() : Ok(access);
    }

    /// <summary>Every client this person belongs to, across companies.</summary>
    [HttpGet("accounts/{accountId:guid}/clients")]
    [ProducesResponseType(typeof(List<ClientAccess>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForAccount(Guid accountId, CancellationToken token) =>
        Ok(await _access.ListForUserAsync(accountId, token));

    /// <summary>Creates the client, or updates its name, HRIS id and active flag.</summary>
    [HttpPut("clients/{clientEntityId:guid}")]
    public async Task<IActionResult> Upsert(Guid clientEntityId, [FromBody] UpsertClientEntityRequestBody request, CancellationToken token)
    {
        try
        {
            await _entities.UpsertAsync(request.TenantId, clientEntityId, request.ExternalId, request.Name, request.Active, token);
            await _entities.CommitChangesAsync(token);
            return Ok();
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }
}

public record UpsertClientEntityRequestBody
{
    public Guid TenantId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
}
