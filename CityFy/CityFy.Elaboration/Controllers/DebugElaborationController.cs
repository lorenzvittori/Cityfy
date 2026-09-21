using CityFy.Elaboration.Models;
using CityFy.Elaboration.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Elaboration.Controllers
{
    [ApiController]
    [Route("debug/elaboration")]
    public class DebugElaborationController : ControllerBase
    {
        private readonly IElaborationService _service;

        public DebugElaborationController(IElaborationService service)
        {
            _service = service;
        }

        // POST debug/elaboration/process
        // Body: { "seedTag": "rock" }  (seedTag optional; if omitted process all TagGraph docs)
        [HttpPost("process")]
        public IActionResult StartProcess([FromBody] ProcessRequest? req)
        {
            try
            {
                Task.Run(() => _service.ProcessAsync(req?.SeedTag));
                return Accepted();
            }
            catch (Exception ex)
            {
                Console.WriteLine("StartProcess failed: " + ex);
                return StatusCode(500);
            }
        }

        // GET debug/elaboration/process?seedTag=rock  -> run synchronously and return produced graphs
        [HttpGet("process")]
        public async Task<IActionResult> ProcessNow([FromQuery] string? seedTag)
        {
            try
            {
                var res = await _service.ProcessAsync(seedTag);
                return Ok(res);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ProcessNow failed: " + ex);
                return StatusCode(500);
            }
        }
    }
}
