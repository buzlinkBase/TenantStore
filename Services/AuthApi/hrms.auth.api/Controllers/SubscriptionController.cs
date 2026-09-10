using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Api.RequestModels;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status403Forbidden)]
    public class SubscriptionController : ControllerBase
    {
        private readonly SubscriptionService _service;
        private readonly MembershipGrpcClient _membershipGrpcClient;
        public SubscriptionController(SubscriptionService service, MembershipGrpcClient membershipGrpcClient, IOptions<Domains> options)
        {
            _service = service;
            _membershipGrpcClient = membershipGrpcClient;
        }

        [HttpPost()]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromBody] PlanRequestDto request, CancellationToken token)
        {
            var userId = HttpContext.User.GetRequiredUserId();
            var tenantId = HttpContext.User.GetUserClaim("TenantId")?.ToString() ?? "";

            if (string.IsNullOrEmpty(tenantId) || Guid.Parse(tenantId) == Guid.Empty)
                return BadRequest("Invalid tenant context.");

            if (userId == Guid.Empty)
                return Unauthorized();

            // The JWT carries no role claim (see JwtService.CreateTokenAsync) -- roles live in
            // TenantApi's UserMembership, resolved here the same way UserService.SetDefaultTenant
            // does for the same kind of cross-service check.
            var membership = await _membershipGrpcClient.ResolveMembershipAsync(userId, Guid.Parse(tenantId));
            if (!membership.Success || !membership.Found ||
                !(membership.Roles.Contains("Owner") || membership.Roles.Contains("Admin")))
                return Forbid();

            await _service.Create(new PlanRequest
            {
                PlanId = request.PlanId,
                ValidUntil = request.ValidUntil,
                TenantId = Guid.Parse(tenantId),
                UserId = userId
            }, token);
            return Ok();
        }
    }
}
