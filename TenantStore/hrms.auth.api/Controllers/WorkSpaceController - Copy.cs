using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib.DTO;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly SubscriptionService _service;
        public SubscriptionController(SubscriptionService service, 
            IOptions<Domains> options)
        {
            _service = service;
        }

        [HttpPost()]
        public async Task<IActionResult> Create(PlanRequest payload, CancellationToken token)
        {
            payload.UserId = HttpRequest.get;
            await _service.Create(payload,token);
            return Ok(); 
        }

    }
}