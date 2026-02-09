using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace DTR.Api.Controllers;

[ApiVersionNeutral]
[ApiController]
[Route("iclock")] // Sets the base route to match ZKTeco's hardcoded paths
public class AdmsController : ControllerBase
{
    private readonly ILogger<AdmsController> _logger;
    public AdmsController(ILogger<AdmsController> logger)
    {
        _logger = logger;
    }

    // HANDSHAKE & HEARTBEAT
    // URL: GET /iclock/getrequest?SN=XYZ123
    [HttpGet("getrequest")]
    public IActionResult GetRequest([FromQuery] string SN)
    {
        _logger.LogInformation("Heartbeat received from Device SN: {SN}", SN);

        // You can return commands here. 
        // Example: "C:101:SET OPTIONS DateTime=2024-01-01 12:00:00"
        // Return "OK" to tell the device there are no pending commands.
        return Content("OK", "text/plain");
    }

    // DATA RECEIVER (Attendance Logs, User Info, etc.)
    // URL: POST /iclock/cdata?SN=XYZ123&table=ATTLOG
    [HttpPost("cdata")]
    public async Task<IActionResult> PostData([FromQuery] string SN, [FromQuery] string table)
    {

        using var reader = new StreamReader(Request.Body);
        string rawBody = await reader.ReadToEndAsync();

        if (table == "ATTLOG")
        {
            _logger.LogInformation("Processing Attendance Logs for SN: {SN}", SN);
            await ParseAttendanceLogs(rawBody);
        }

        // IMPORTANT: Must return "OK" so the device clears its local buffer
        return Content("OK", "text/plain");

    }

    private async Task ParseAttendanceLogs(string data)
    {
        var lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var attendances = new List<Attendance>();

        foreach (var line in lines)
        {
            var fields = line.Split('\t');
            if (fields.Length >= 2)
            {
                var attendance = new Attendance()
                {
                    WorkDateTime = DateTime.Parse(fields[1]),
                    BioId = int.Parse(fields[1]),
                };
                attendances.Add(attendance);
            }
        }

        //await _service.Create(attendances);

    }
}
