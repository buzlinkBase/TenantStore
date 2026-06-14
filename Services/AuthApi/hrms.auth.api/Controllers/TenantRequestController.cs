using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class TenantRequestController : ControllerBase
    {
        private readonly TenantRequestService _service;

        public TenantRequestController(TenantRequestService service)
        {
            _service = service;
        }

        [HttpGet("status/{tenantId:guid}")]
        [ProducesResponseType(typeof(TenantStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatus(Guid tenantId, CancellationToken token)
        {
            var status = await _service.FindOne(tenantId);
            if (status == null) return NotFound();
            return Ok(new TenantStatusResponse
            {
                TenantId = status.TenantId,
                Status = status.Status.ToString(),
                IsReady = status.Status == TenantCreationStatus.Created,
            });
        }
    }
}
