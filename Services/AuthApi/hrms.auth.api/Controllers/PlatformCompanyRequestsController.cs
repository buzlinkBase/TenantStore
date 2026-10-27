using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Platform;
using OnePunch.Auth.Api.Extensions;
using OnePunch.Auth.Api.RateLimiting;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// Sets up a company for an account owner from the Platform Admin Console. Requires
/// <c>platform.companies.create</c>, authenticated with the platform scheme only -- a company
/// user's token is rejected before anything else is looked at.
/// </summary>
[Route("api/v{version:apiVersion}/platform/company-requests")]
[ApiVersion("1.0")]
[ApiController]
[Authorize(AuthenticationSchemes = PlatformAuthDefaults.Scheme, Policy = PlatformPermissions.CompaniesCreate)]
[EnableRateLimiting(RateLimitPolicies.PlatformAdmin)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status403Forbidden)]
public class PlatformCompanyRequestsController(PlatformCompanyService companies) : ControllerBase
{
    /// <summary>Creates a company owned by <c>ownerUserId</c>; it finishes setting up in the background.</summary>
    [HttpPost("{ownerUserId:guid}")]
    [ProducesResponseType(typeof(PlatformCompanyRequestResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid ownerUserId, [FromBody] CreateWorkspaceRequest payload, CancellationToken ct) =>
        Ok(await companies.CreateForOwnerAsync(ownerUserId, payload, User.FindFirst("name")?.Value ?? User.GetRequiredUserId().ToString(), ct));
}
