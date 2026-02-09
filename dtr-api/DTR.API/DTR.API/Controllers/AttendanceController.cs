using Asp.Versioning;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;

namespace DTR.Api.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[ApiController]
public class AttendanceController : ControllerBase
{
    private readonly AttendanceService _attendanceService;
    private readonly DailyRecordService _service;

    public AttendanceController(
        AttendanceService attendanceService,
        DailyRecordService service )
    {
        _attendanceService = attendanceService;
        _service = service;
    }


    [HttpPost("upload-att-log")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] int branchId)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (Request.Headers.ContainsKey("Authorization"))
        {
            // 2. Get the header value
            var authorizationHeader = Request.Headers["Authorization"].FirstOrDefault();

            // 3. Ensure it starts with "Bearer " and extract the token part
            if (authorizationHeader != null && authorizationHeader.StartsWith("Bearer "))
            {
                // The token starts after the "Bearer " prefix (7 characters long)
                string token = authorizationHeader.Substring("Bearer ".Length).Trim();
                return Ok(new { Token = token, Message = "Token retrieved successfully." });
            }
        }

        


        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        var extension = Path.GetExtension(file.FileName).ToLower();
        var parser = ParserFactory.Create(extension);
        var parsedData = await parser.Parse(memoryStream);

        //TODO separate unregistered and registered att here
        var atts = parsedData.Select(x => new Attendance
        {
            BioId =  x.Id,
        }).ToList();
        _attendanceService.AddRange(atts);
        _attendanceService.CommitChanges();
        return Ok("Success");

    }
}


public static class ParserFactory
{
    public static IFileParser Create(string extension)
    {
        return extension switch
        {
            ".dat" => new DatParser(),
            //".csv" => new CsvParser(),
            //".txt" => new TxtParser(),
            //".xls" or ".xlsx" => new ExcelParser(),
            _ => throw new NotSupportedException($"File type {extension} not supported")
        };
    }
}
public interface IFileParser
{
    Task<List<CreateAttLog>> Parse(Stream stream);
}
public class DatParser : IFileParser
{
    public async Task<List<CreateAttLog>> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // Split into lines
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var parsedLogs = new List<CreateAttLog>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var id) && DateTime.TryParse(parts[1], out var timestamp))
            {
                parsedLogs.Add(new CreateAttLog
                {
                    Id = id,
                    Timestamp = timestamp
                });
            }
        }
        return parsedLogs;
    }
}