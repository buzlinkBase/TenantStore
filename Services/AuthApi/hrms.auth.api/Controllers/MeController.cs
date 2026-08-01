using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.Controllers
{
    /// <summary>
    /// Flow I (Fetch Account Associated Tenants): a decoupled, non-blocking call for the
    /// tenant switcher UI, separate from the login response's best-effort inline tenant list.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]
    public class MeController : ControllerBase
    {
        private readonly MembershipCacheService _membershipCacheService;

        public MeController(MembershipCacheService membershipCacheService)
        {
            _membershipCacheService = membershipCacheService;
        }

        [HttpGet("tenants")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTenants()
        {
            var userId = HttpContext.User.GetRequiredUserId();
            var tenants = await _membershipCacheService.GetMembershipsAsync(userId);
            return Ok(tenants);
        }
    }
}
