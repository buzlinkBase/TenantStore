using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace NotificationService.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    public class EmailQueryController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new {
                Message="Success" 
            }); 
        }
    }
}
