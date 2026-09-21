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
        // Body: { "tagGraphs": [ ... ] }  - fornire i TagGraph da processare. L'elaboration non recupera collection di altri progetti.
        [HttpPost("process")]
        public IActionResult StartProcess([FromBody] ProcessRequest? req)
        {
            try
            {
                if (req?.TagGraphs == null)
                    return BadRequest("Elaboration non deve recuperare collection di altri progetti. Fornire i TagGraph nel body come 'TagGraphs'.");

                Task.Run(() => _service.ProcessAsync(req.TagGraphs));
                return Accepted();
            }
            catch (Exception ex)
            {
                Console.WriteLine("StartProcess failed: " + ex);
                return StatusCode(500);
            }
        }

        // GET debug/elaboration/process -> non supportato: usare POST con TagGraphs forniti
        [HttpGet("process")]
        public IActionResult ProcessNow([FromQuery] string? seedTag)
        {
            return BadRequest("Operazione non supportata. Elaboration non deve recuperare collection di altri progetti. Usare POST con il body contenente 'TagGraphs'.");
        }

        // POST debug/elaboration/process-remote
        // Body: { "baseUrl": "https://localhost:5001", "pageSize": 50 }
        [HttpPost("process-remote")]
        public IActionResult StartRemoteProcess([FromBody] RemoteProcessRequest? req)
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.BaseUrl))
                    return BadRequest("Provide 'BaseUrl' in request body.");

                Task.Run(() => _service.ProcessFromRemoteAsync(req.BaseUrl!, req.PageSize));
                return Accepted();
            }
            catch (Exception ex)
            {
                Console.WriteLine("StartRemoteProcess failed: " + ex);
                return StatusCode(500);
            }
        }
    }
}
