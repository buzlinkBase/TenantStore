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
public class TenantController : ControllerBase
{
    private readonly TenantService _service;
    private readonly ApiKeySetting _apiSettings;
    private readonly IMapper _mapper;

    public TenantController(TenantService service,
        IOptions<ApiKeySetting> apiSettings,
        IMapper mapper)
    {
        _service = service;
        _apiSettings = apiSettings.Value;
        _mapper = mapper;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] CreateTenant payload)
    {
        await _service.RegisterAsync(payload);
        return Ok();
    }

    [HttpPost("winform-register")]
    [AllowAnonymous]
    public async Task<IActionResult> ManualRegister([FromBody] CreateTenant payload)
    {
        var tenant = await _service.RegisterAsync(payload);
        return Ok(tenant);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenant payload)
    {
        await _service.UpdateAsync(id, payload);
        return Ok();
    }
    [HttpGet()]
    public async Task<ActionResult<TenantModel>> GetAll()
    {
        var tenantResult = await _service.FindAll();
        var tenants = _mapper.Map<List<TenantModel>>(tenantResult);
        return Ok(tenants);
    }
    [HttpGet("{Id:guid}")]
    public async Task<ActionResult<TenantModel>> FindOne(Guid Id)
    {
        var tenantResult = await _service.FindTenant(Id);
        if (tenantResult == null)
        {
            return NotFound();
        }
        var tenant = _mapper.Map<TenantModel>(tenantResult);
        return Ok(tenant);
    }

    [HttpPost("upload-att-log")]
    [Consumes("multipart/form-data")]
    [AllowAnonymous]
    public async Task<ActionResult<List<AttendanceLog>>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();

        // Split into lines
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var parsedLogs = new List<AttendanceLog>();

        foreach (var line in lines)
        {
            // Split each line by tab
            var parts = line.Split('\t');
            var log = new AttendanceLog
            {
                Id = int.Parse(parts[0]),
                Timestamp = DateTime.Parse(parts[1]),
                //Col1 = parts[2],
                //Col2 = parts[3],
                //Name = parts[4],
                //Status = parts[5], // "I" or "O"
                //Extra1 = parts[6],
                //Extra2 = parts[7]
            };
            parsedLogs.Add(log);
        }

        // Example: print parsed logs
        //foreach (var log in parsedLogs)
        //{
        //    Console.WriteLine($"{log.Id} - {log.Name} - {log.Timestamp} - {log.Status}");
        //}

        return Ok(parsedLogs);
    }

}
public class AttendanceLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } 
}