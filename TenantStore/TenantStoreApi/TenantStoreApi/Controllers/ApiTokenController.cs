using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class ApiTokenController : ControllerBase
{
    private readonly ApiTokenService _service;
    public ApiTokenController(ApiTokenService service )
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateToken payload)
    {
        var response = await _service.GenerateToken(payload);
        return Ok(response);
    }

    //[HttpGet]
    //public async Task<IActionResult> GetAll()
    //{
    //    return Ok();
    //}

    //Delete
}