using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace DTR.Api.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiController]
public class DTRController : ControllerBase
{
    private readonly DTRCalcService _service;
    private readonly WorkPlanScheduleService _employeeService;

    public DTRController(
        DTRCalcService service,
        WorkPlanScheduleService employeeService)
    {
        _service = service;
        _employeeService = employeeService;
    }

    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate([FromBody] DTRRequestPayload payload, CancellationToken token)
    {
        var data = await _service.GetDTRInfo<DailyRecord>(payload, ProcessorType.DTRDetail, token);
        return Ok(data);
    }


    [HttpGet("one")]
    public async Task<IActionResult> One([FromQuery] DateOnly date, CancellationToken token)
    { 
        var data = await _employeeService.GetAllCustomShiftsAync(date, date);
        return Ok(data);
    }

}