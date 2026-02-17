using Asp.Versioning;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib;

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

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] CreateTenant payload, CancellationToken token)
    {
        await _service.RegisterAsync(payload, token);
        return Ok();
    }

    [HttpPost("winform-register")]
    [AllowAnonymous]
    public async Task<IActionResult> ManualRegister([FromBody] CreateTenant payload, CancellationToken token)
    {
        var tenant = await _service.RegisterAsync(payload, token);
        return Ok(tenant);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, [FromBody] UpdateTenant payload, CancellationToken token)
    {
        await _service.UpdateAsync(id, payload, token);
        return Ok();
    }
    [HttpGet()]
    public async Task<ActionResult<TenantModel>> FindAll()
    {
        var tenantResult = await _service.FindAll();
        var tenants = _mapper.Map<List<TenantModel>>(tenantResult);
        return Ok(tenants);
    }

    [HttpGet("{Id:guid}")]
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
