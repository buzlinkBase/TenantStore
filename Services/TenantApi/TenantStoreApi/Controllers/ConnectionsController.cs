using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiController]
[Authorize]
public class ConnectionsController : ControllerBase
{
    private readonly ConnectionService _service;
    public ConnectionsController(ConnectionService service)
    {
        _service = service;
    }

    [HttpGet()]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ConnectionStringResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery(Name = "tenant-id")] Guid tenantId,
        [FromQuery(Name = "service")] string service,
        CancellationToken token)
    {
        var connectionStr = await _service.GetConnection(tenantId, service);
        return Ok(connectionStr);
    }
}
