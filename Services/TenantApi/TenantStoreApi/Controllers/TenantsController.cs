using TenantStoreApi.Core.Filters;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class TenantsController : ControllerBase
{
    private readonly TenantService _service;
    private readonly IMapper _mapper;
    private readonly UserMembershipService _membershipService;
    private readonly ITenantProvider _tenantProvider;

    public TenantsController(TenantService service,
        IMapper mapper,
        UserMembershipService membershipService,
        ITenantProvider tenantProvider)
    {
        _service = service;
        _mapper = mapper;
        _membershipService = membershipService;
        _tenantProvider = tenantProvider;
    }

    // Renames the caller's current company. Called by HRIS when Company Info's "Company Name" is
    // saved, forwarding the caller's own bearer token -- so the caller must be an active member
    // of this tenant with at least one role beyond the portal-only ones, the same audience HRIS
    // lets onto the Company Settings screen. The tenant always comes from the token, never the
    // request, so no one can rename a company they don't belong to.
    [HttpPut("current/name")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameCurrent([FromQuery] string name, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        if (!await _membershipService.CanManageCompanyAsync(User.GetRequiredUserId(), tenantId, token))
            return Forbid();

        try
        {
            await _service.RenameAsync(tenantId, name ?? "", token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    // Updates the console-facing contact details (address, contact number) of the caller's current
    // company. Same caller rules as RenameCurrent; the tenant always comes from the token.
    [HttpPut("current/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCurrentProfile([FromBody] UpdateTenantProfile payload, CancellationToken token)
    {
        var tenantId = _tenantProvider.TenantId;
        if (tenantId == Guid.Empty) return BadRequest("Tenant context is required.");

        if (!await _membershipService.CanManageCompanyAsync(User.GetRequiredUserId(), tenantId, token))
            return Forbid();

        try
        {
            await _service.UpdateProfileAsync(tenantId, payload.Address, payload.Contact, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Put(Guid id, [FromBody] UpdateTenant payload, CancellationToken token)
    {
        await _service.UpdateAsync(id, payload, token);
        return Ok();
    }

    // The caller's own companies -- a locked-out member still needs to switch away.
    [AllowWhenLocked]
    [HttpGet()]
    [ProducesResponseType(typeof(List<TenantModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TenantModel>>> FindAll(CancellationToken token)
    {
        var userId = User.GetRequiredUserId();
        var tenantResult = await _service.FindAlltenants(userId, token);
        var tenants = _mapper.Map<List<TenantModel>>(tenantResult);
        return Ok(tenants);
    }

    [HttpGet("{Id:guid}")]
    [ProducesResponseType(typeof(TenantModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantModel>> FindOne(Guid Id, CancellationToken token)
    {
        var tenantResult = await _service.FindTenantAsync(Id, token);
        if (tenantResult == null)
        {
            return NotFound();
        }
        var tenant = _mapper.Map<TenantModel>(tenantResult);
        return Ok(tenant);
    }

}
