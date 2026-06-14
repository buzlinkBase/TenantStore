using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class OrganizationPlanController : ControllerBase
{

    [HttpPost]
    [Authorize(Roles = "Provider")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] OrgPlanRequest payload)
    {
        return Ok();
    }
}


public record OrgPlanRequest
{
    public string Name { get; set; }
    public string Key { get; set; }
    public bool FreeTrial { get; set; }
    public int Days { get; set; }
    public bool AvailablePublicly { get; set; }

    public bool Monthly { get; set; }
    public decimal MonthlyRate { get; set; }
    public bool Yearly { get; set; }
    public decimal YearlyRate { get; set; }
    public bool SeatBase { get; set; }
    public int SeatCount { get; set; }
    public List<Features> Features { get; set; }
}


public record Features
{
    public string Name { get; set; }
    public string Key { get; set; }
    public string Description { get; set; }
    public bool AvailablePublicly { get; set; }

}
