using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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

    public TenantsController(TenantService service,
        IMapper mapper)
    {
        _service = service;
        _mapper = mapper;
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Put(Guid id, [FromBody] UpdateTenant payload, CancellationToken token)
    {
        await _service.UpdateAsync(id, payload, token);
        return Ok();
    }

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
