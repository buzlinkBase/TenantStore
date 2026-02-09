using Adms.api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace OnePunch.Adms.Properties.Controllers;

public record AttendanceRequest(string SN,Guid tenantId);

[Route("api/[controller]")]
[ApiController]
public class AttendanceController : ControllerBase
{
    private readonly AttendanceService _service;

    public AttendanceController(AttendanceService service)
    {
        _service = service;
    }
    [HttpGet]
    public async Task<IActionResult> Download([FromQuery] AttendanceRequest request)
    {
        var att = await _service.Download(request);
        return Ok(att);
    }

    [HttpPost]
    public async Task<IActionResult> Upload([FromQuery] AttendanceRequest request)
    {
        var att = await _service.Download(request);
        return Ok(att);
    }
}
