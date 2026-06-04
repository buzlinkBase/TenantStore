using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core;
using Onepunch.Auth.Domain.DTOs;

namespace OnePunch.Auth.Api.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
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
        public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest payload, CancellationToken token)
        {
            var result=await _service.Create(payload,User, token);
            return Ok(result); 
        }
    }
}