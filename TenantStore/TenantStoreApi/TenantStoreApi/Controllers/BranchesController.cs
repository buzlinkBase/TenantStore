using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace TenantStoreApi.Controllers
{
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [ApiController]
    public class BranchesController : ControllerBase
    {
        private readonly BranchService _service;

        public BranchesController(BranchService service)
        {
            _service = service;
        }

        [HttpGet("tenant")]
        public async Task<IActionResult> Get([FromQuery(Name = "tenant-id")] Guid tenantId)
        {
            var result = await _service.FindAllAsync(tenantId);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetOne(Guid id, CancellationToken token)
        {
            var result = await _service.FineOneAsync(id, token);
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] CreateBranch value, CancellationToken token)
        {
            await _service.AddAsync(value, token);
            await _service.CommitChangesAsync(token);
            return Ok();
        }

        // PUT api/<BranchController>/5
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Put(Guid id, [FromBody] UpdateBranch value, CancellationToken token)
        {
            await _service.UpdateAsync(id, value, token);
            await _service.CommitChangesAsync(token);
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
