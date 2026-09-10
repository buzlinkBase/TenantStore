using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Controllers;

// Read-only -- the feature/action catalog is system-seeded (PermissionCatalogSeederService),
// never admin-typed free text.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class PermissionsController : ControllerBase
{
    private readonly PermissionService _service;

    public PermissionsController(PermissionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var data = await _service.FindAllAsync(token);
        return Ok(data);
    }
}
