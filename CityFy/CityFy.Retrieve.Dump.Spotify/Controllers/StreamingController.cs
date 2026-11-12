using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Retrieve-Dump-Spotify")]
[Route("api/upload")]
public sealed class StreamingController(IUploadTaskService tasks) : ControllerBase
{
    // GET api/upload/tasks/{uploadId} — status of an upload (errorCode when Failed)
    [HttpGet("tasks/{uploadId}")]
    public async Task<ActionResult<ProcessingTask>> GetTaskByUploadId(string uploadId, CancellationToken ct)
    {
        var task = await tasks.GetAsync(uploadId, ct);
        return task == null
            ? this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound)
            : Ok(task);
    }

    // GET api/upload/tasks?page=1&pageSize=20
    [HttpGet("tasks")]
    public async Task<ActionResult<IReadOnlyList<ProcessingTask>>> GetTasksPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await tasks.ListAsync(page, pageSize, ct));

    // GET api/upload/collections/{collectionName}?page=1&pageSize=50&uploadId=...
    [HttpGet("collections/{collectionName}")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCollectionDocuments(string collectionName,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? uploadId = null, CancellationToken ct = default) =>
        Ok(await tasks.GetDocumentsAsync(collectionName, page, pageSize, uploadId, ct));
}
