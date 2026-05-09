using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Api.RequestModels;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly SubscriptionService _service;
        public SubscriptionController(SubscriptionService service, IOptions<Domains> options)
        {
            _service = service;
        }

        [HttpPost()]
        public async Task<IActionResult> Create(PlanRequestDto request, CancellationToken token)
        {
            var userId = HttpContext.User.GetRequiredUserId();
            var tenantId = HttpContext.User.GetUserClaim("TenantId")?.ToString() ?? "";
            if (string.IsNullOrEmpty(tenantId) || Guid.Parse(tenantId) == Guid.Empty)
            {
                return BadRequest("Invalid tenant");
            }
            else if (userId == Guid.Empty)
            {
                return Unauthorized();
            }
            var payload = new PlanRequest
            {
                PlanId = request.PlanId,
                ValidUntil = request.ValidUntil,
                TenantId = Guid.Parse(tenantId),
                UserId = userId
            };
            await _service.Create(payload, token);
            return Ok();

        }
    }
}