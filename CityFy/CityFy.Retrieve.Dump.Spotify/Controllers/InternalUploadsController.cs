using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Controllers;

/// <summary>
/// Service-to-service endpoints used by CityFy.Api (user uploads, admin panel). The API
/// authenticates the user and passes the user id; this service is not reachable from outside.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/uploads")]
public sealed class InternalUploadsController(IZipUploadService uploads, IUploadTaskService tasks, IUploadProcessor processor) : ControllerBase
{
    /// <summary>
    /// Kestrel (30 MB) and multipart (128 MB) default limits are below Upload:MaxUploadSizeBytes
    /// (200 MB, checked by the service with UPLOAD_TOO_LARGE): raised here, slightly above it.
    /// </summary>
    public const long MaxRequestBytes = 220_000_000;

    // POST internal/uploads (multipart: file, userId) — store and extract the zip, the import runs in background
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<UploadResult>> Upload([FromForm] UploadZipApi api, CancellationToken ct)
    {
        var result = await uploads.HandleZipUploadAsync(api.File!, api.UserId ?? "", ct);
        return AcceptedAtAction(nameof(Get), new { uploadId = result.Id }, result);
    }

    // GET internal/uploads/{uploadId} — status of an upload (errorCode when Failed)
    [HttpGet("{uploadId}")]
    public async Task<ActionResult<ProcessingTask>> Get(string uploadId, CancellationToken ct)
    {
        var task = await tasks.GetAsync(uploadId, ct);
        return task == null
            ? this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound)
            : Ok(task);
    }

    // GET internal/uploads?page=1&pageSize=20 — uploads of every user, most recent first (admin panel)
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProcessingTask>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await tasks.ListAsync(page, pageSize, ct));

    // POST internal/uploads/{uploadId}/cancel — admin: stop a queued or running import
    [HttpPost("{uploadId}/cancel")]
    public async Task<ActionResult<UploadCancelledResponse>> Cancel(string uploadId)
    {
        await processor.CancelAsync(uploadId);
        return Accepted(new UploadCancelledResponse(uploadId, nameof(ProcessingStatus.Cancelled)));
    }
}
