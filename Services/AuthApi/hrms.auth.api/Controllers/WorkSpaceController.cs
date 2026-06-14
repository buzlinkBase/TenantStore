using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseModel<ProblemDetails>), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public class WorkSpaceController : ControllerBase
    {
        private readonly WorkspaceService _service;
        public WorkSpaceController(WorkspaceService service,
            IOptions<Domains> options)
        {
            _service = service;
        }

        [HttpPost()]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest payload, CancellationToken token)
        {
            var result=await _service.Create(payload,User, token);
            return Ok(result);
        }
    }
}
