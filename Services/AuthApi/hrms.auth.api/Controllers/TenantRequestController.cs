using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OnePunch.Auth.Api.Controllers
{

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    public class TenantRequestController : ControllerBase
    {
        private readonly TenantRequestService _service;

        public TenantRequestController(TenantRequestService service )
        {
            _service = service;
        }

        //public async Task<IActionResult>
    }
}