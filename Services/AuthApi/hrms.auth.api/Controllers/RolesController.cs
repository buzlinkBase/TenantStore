using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.Controllers
{

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [Authorize]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]
    public class RolesController : ControllerBase
    {
        private readonly RoleService _service;
        public RolesController(RoleService service, IOptions<Domains> options)
        {
            _service = service;
        }

        [HttpPost()]
        [ProducesResponseType(typeof(ResponseModel<Role>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromQuery] string roleName, CancellationToken token)
        {
            var role = await _service.Create(roleName);
            return Ok(role);
        }

        [HttpGet()]
        [ProducesResponseType(typeof(ResponseModel<List<string>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(CancellationToken token)
        {
            var roles = await _service.GetAll();
            return Ok(roles.Select(x => x.Name).ToArray());
        }
    }
}
