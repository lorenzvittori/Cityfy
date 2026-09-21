using System.IO;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Retrieve.Dump.Spotify.Controllers
{
    [ApiController]
    [ApiExplorerSettings(GroupName = "Debug-Dump-Spotify")]
    [Route("debug/upload")]
    public class DebugZipController : ControllerBase
    {
        private readonly IZipUploadService _zipService;
        private readonly CityFy.Retrieve.Dump.Spotify.Repositories.IStreamingRepository _repo;
        private readonly Microsoft.Extensions.Logging.ILogger<DebugZipController> _logger;

        public DebugZipController(IZipUploadService zipService, CityFy.Retrieve.Dump.Spotify.Repositories.IStreamingRepository repo, Microsoft.Extensions.Logging.ILogger<DebugZipController> logger)
        {
            _zipService = zipService;
            _repo = repo;
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

        // GET debug/upload/tasks/{uploadId}
        [HttpGet("tasks/{uploadId}")]
        public async Task<IActionResult> GetTaskByUploadId([FromRoute] string uploadId)
        {
            if (string.IsNullOrWhiteSpace(uploadId)) return BadRequest("Missing uploadId");
            try
            {
                var task = await _repo.GetTaskByUploadIdAsync(uploadId);
                if (task == null) return NotFound();
                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTaskByUploadId failed: {Message}", ex.Message);
                return StatusCode(500);
            }
        }

        // GET debug/upload/tasks?page=1&pageSize=20
        [HttpGet("tasks")]
        public async Task<IActionResult> GetTasksPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var list = await _repo.GetTasksPagedAsync(page, pageSize);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTasksPaged failed: {Message}", ex.Message);
                return StatusCode(500);
            }
        }

        // GET debug/upload/collection/{collectionName}?page=1&pageSize=20&uploadId=...
        [HttpGet("collection/{collectionName}")]
        public async Task<IActionResult> GetCollectionDocuments([FromRoute] string collectionName, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? uploadId = null)
        {
            if (string.IsNullOrWhiteSpace(collectionName)) return BadRequest("Missing collectionName");
            try
            {
                var docs = await _repo.GetDocumentsPagedAsync(collectionName, page, pageSize, uploadId);
                return Ok(docs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCollectionDocuments failed: {Message}", ex.Message);
                return StatusCode(500);
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
