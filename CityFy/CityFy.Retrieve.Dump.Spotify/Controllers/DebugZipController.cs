using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.Controllers;

// Upload, status and cancel are in InternalUploadsController (used by CityFy.Api); these are inspection tools.
[ApiController]
[Route("debug/upload")]
public sealed class DebugZipController(IZipUploadService uploads, IUploadTaskService tasks, IUploadProcessor processor) : ControllerBase
{
    // GET debug/upload/collection/{collectionName}?page=1&pageSize=20&uploadId=...
    [HttpGet("collection/{collectionName}")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCollectionDocuments(string collectionName,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? uploadId = null, CancellationToken ct = default) =>
        Ok(await tasks.GetDocumentsAsync(collectionName, page, pageSize, uploadId, ct));

    // POST debug/upload/migrate/{uploadId}?userId=...
    // Copies a legacy 'streaming_history_{uploadId}' collection (pre listening_events)
    // into 'listening_events' for the given user. Idempotent. 400 UPLOAD_USER_MISSING without userId.
    [HttpPost("migrate/{uploadId}")]
    public async Task<ActionResult<LegacyMigrationResponse>> MigrateLegacyUpload(string uploadId, [FromQuery] string? userId, CancellationToken ct)
    {
        var inserted = await processor.MigrateLegacyUploadAsync(uploadId, userId ?? "", ct);
        return Ok(new LegacyMigrationResponse(uploadId, userId!.Trim(), inserted));
    }

    // GET debug/upload/zip/files/{id}
    [HttpGet("zip/files/{id}")]
    public ActionResult<UploadResult> ListExtractedFiles(string id)
    {
        var result = uploads.GetUploadResult(id);
        return result == null
            ? this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{id}' not found or not extracted yet", StatusCodes.Status404NotFound)
            : Ok(result);
    }
}
