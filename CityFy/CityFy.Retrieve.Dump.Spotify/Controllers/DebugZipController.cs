using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.Controllers
{
    // Errors are ProblemDetails with a stable "code" (ServiceDefault.Errors): CityFyException
    // thrown by the services and unexpected exceptions are converted by the shared handler.
    [ApiController]
    [Route("debug/upload")]
    public class DebugZipController : ControllerBase
    {
        private readonly IZipUploadService _zipService;
        private readonly IStreamingRepository _repo;

        public DebugZipController(IZipUploadService zipService, IStreamingRepository repo)
        {
            _zipService = zipService;
            _repo = repo;
        }

        // POST debug/upload/zip (multipart: file, userId)
        // Kestrel (30 MB) and multipart (128 MB) default limits are below DebugUpload:MaxUploadSizeBytes
        // (200 MB, checked by the service with UPLOAD_TOO_LARGE): raised here, slightly above it.
        [HttpPost("zip")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(220_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 220_000_000)]
        public async Task<IActionResult> UploadZip([FromForm] UploadZipApi api) =>
            Ok(await _zipService.HandleZipUploadAsync(api.File!, api.UserId ?? ""));

        // GET debug/upload/tasks/{uploadId} — status of an upload (errorCode when FAILED)
        [HttpGet("tasks/{uploadId}")]
        public async Task<IActionResult> GetTaskByUploadId([FromRoute] string uploadId)
        {
            var task = await _repo.GetTaskByUploadIdAsync(uploadId);
            return task == null
                ? this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound)
                : Ok(task);
        }

        // GET debug/upload/tasks?page=1&pageSize=20
        [HttpGet("tasks")]
        public async Task<IActionResult> GetTasksPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            Ok(await _repo.GetTasksPagedAsync(page, pageSize));

        // GET debug/upload/collection/{collectionName}?page=1&pageSize=20&uploadId=...
        [HttpGet("collection/{collectionName}")]
        public async Task<IActionResult> GetCollectionDocuments([FromRoute] string collectionName, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? uploadId = null) =>
            Ok(await _repo.GetDocumentsPagedAsync(collectionName, page, pageSize, uploadId));

        // POST debug/upload/migrate/{uploadId}?userId=...
        // Copies a legacy 'streaming_history_{uploadId}' collection (pre listening_events)
        // into 'listening_events' for the given user. Idempotent.
        [HttpPost("migrate/{uploadId}")]
        public async Task<IActionResult> MigrateLegacyUpload(
            [FromRoute] string uploadId,
            [FromQuery] string? userId,
            [FromServices] IStreamingBlPersistenceService persistence,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return this.CityFyProblem(ErrorCodes.UploadUserMissing, "userId is required", StatusCodes.Status400BadRequest);

            var inserted = await persistence.MigrateLegacyUploadAsync(uploadId, userId.Trim(), ct);
            return Ok(new { uploadId, userId, inserted });
        }

        // GET debug/upload/zip/files/{id}
        [HttpGet("zip/files/{id}")]
        public IActionResult ListExtractedFiles(string id)
        {
            var result = _zipService.GetUploadResult(id);
            return result == null
                ? this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{id}' not found or not extracted yet", StatusCodes.Status404NotFound)
                : Ok(result);
        }
    }
}
