using CityFy.Retrieve.Dump.Spotify.Repositories;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Retrieve-Dump-Spotify")]
[Route("api/upload")]
public class StreamingController : ControllerBase
{
    private readonly IStreamingRepository _repo;

    public StreamingController(IStreamingRepository repo)
    {
        _repo = repo;
    }

    // GET api/upload/tasks/{uploadId} — status of an upload (errorCode when FAILED)
    [HttpGet("tasks/{uploadId}")]
    public async Task<IActionResult> GetTaskByUploadId([FromRoute] string uploadId)
    {
        var task = await _repo.GetTaskByUploadIdAsync(uploadId);
        return task == null
            ? this.CityFyProblem(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound)
            : Ok(task);
    }

    // GET api/upload/tasks
    // Pagination via headers: X-Page, X-Page-Size
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasksPaged()
    {
        var (page, pageSize) = ReadPaging(defaultPageSize: 20);
        var list = await _repo.GetTasksPagedAsync(page, pageSize);
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        return Ok(list);
    }

    // GET api/upload/collections/{collectionName}
    // Pagination via headers and optional X-Upload-Id header to filter
    [HttpGet("collections/{collectionName}")]
    public async Task<IActionResult> GetCollectionDocuments([FromRoute] string collectionName)
    {
        var (page, pageSize) = ReadPaging(defaultPageSize: 50);
        string? uploadId = Request.Headers.TryGetValue("X-Upload-Id", out var uid) ? uid.ToString() : null;

        var docs = await _repo.GetDocumentsPagedAsync(collectionName, page, pageSize, uploadId);
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        return Ok(docs);
    }

    private (int Page, int PageSize) ReadPaging(int defaultPageSize)
    {
        var page = Request.Headers.TryGetValue("X-Page", out var p) && int.TryParse(p.ToString(), out var pv) ? Math.Max(1, pv) : 1;
        var pageSize = Request.Headers.TryGetValue("X-Page-Size", out var ps) && int.TryParse(ps.ToString(), out var psz) ? Math.Max(1, psz) : defaultPageSize;
        return (page, pageSize);
    }
}
