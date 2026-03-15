using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiController]
[Authorize]
public class TenantConnectionController : ControllerBase
{
    private readonly ConnectionService _service;
    public TenantConnectionController(ConnectionService service )
    {
        _service = service;
    }
    [HttpGet()]
    public async Task<IActionResult> Get([FromQuery(Name ="tenant-id")] Guid tenantId,
        CancellationToken token)
    {
        var connectionStr=  await _service.GetConnection(tenantId);
        return Ok(connectionStr);
    }
}
