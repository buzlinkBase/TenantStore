using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    public class BranchController : ControllerBase
    {
        private readonly BranchService _service;

        public BranchController(BranchService service)
        {
            _service = service;
        }

        [HttpGet("tenant/{tenantId:guid}")]
        public async Task<IActionResult> Get(Guid tenantId)
        {
            var result = await _service.FindAllAsync(tenantId);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetOne(Guid id)
        {
            var result = await _service.FineOneAsync(id);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] CreateBranch value)
        {
            await _service.AddAsync(value);
            return Ok();
        }

        // PUT api/<BranchController>/5
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Put(Guid id, [FromBody] UpdateBranch value)
        {
            await _service.UpdateAsync(id, value);
            return Ok();
        }
        //[HttpDelete("{id:guid}")]
        //public async Task<IActionResult> Delete(Guid id)
        //{
        //    var result = await _service.Delete(id);
        //    return Ok(result);
        //}
    }
}
