using Microsoft.AspNetCore.Mvc;

namespace NotificationService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmailQueryController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Redirect("https://mysite-frontend/verified");
        }
    }
}
