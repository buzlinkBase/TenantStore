using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Domain.DTO;

namespace NotificationService.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    public class EmailQueryController : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public IActionResult Get()
        {
            return Ok(new MessageResponse { Message = "Success" });
        }
    }
}
