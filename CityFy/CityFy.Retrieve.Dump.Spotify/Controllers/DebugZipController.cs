using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.RtrieveSpotify.Controllers
{
    [ApiController]
    [Route("debug/upload")]
    public class DebugZipController : ControllerBase
    {
        private readonly IZipUploadService _zipService;
        private readonly Microsoft.Extensions.Logging.ILogger<DebugZipController> _logger;

        public DebugZipController(IZipUploadService zipService, Microsoft.Extensions.Logging.ILogger<DebugZipController> logger)
        {
            _zipService = zipService;
            _logger = logger;
        }

        [HttpPost("zip")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadZip([FromForm] UploadZipApi api)
        {
            var file = api?.File;
            if (file == null)
                return BadRequest("Missing file form field (use 'file')");

            try
            {
                var result = await _zipService.HandleZipUploadAsync(file);
                return Ok(result);
            }
            catch (InvalidDataException)
            {
                return BadRequest("Invalid zip file");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DebugZipController error: {Message}", ex.Message);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("zip/files/{id}")]
        public IActionResult ListExtractedFiles(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Missing id");

            var result = _zipService.GetUploadResult(id);
            if (result == null) return NotFound("Upload id not found or not extracted yet");
            return Ok(result);
        }
    }
}
