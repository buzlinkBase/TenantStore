using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers;

/// <summary>
/// The industry / sub-industry lookup for the create-company and Company Info dropdowns. Any
/// signed-in user can read it -- a brand-new account has no company yet when it creates its first.
/// </summary>
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
[ApiController]
public class IndustriesController : ControllerBase
{
    private readonly IndustryCatalogService _industries;

    public IndustriesController(IndustryCatalogService industries) => _industries = industries;

    [HttpGet]
    [ProducesResponseType(typeof(List<IndustryView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await _industries.GetAllAsync(token));
}
