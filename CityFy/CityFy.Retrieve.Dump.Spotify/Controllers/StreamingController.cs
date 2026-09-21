using CityFy.Retrieve.Dump.Spotify.Repositories;
using Microsoft.AspNetCore.Mvc;

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

    // GET api/upload/tasks/{uploadId}
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
        catch (Exception)
        {
            return StatusCode(500);
        }
    }

    // GET api/upload/tasks
    // Pagination via headers: X-Page, X-Page-Size
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasksPaged()
    {
        int page = 1; int pageSize = 20;
        if (Request.Headers.TryGetValue("X-Page", out var p) && int.TryParse(p.ToString(), out var pv)) page = Math.Max(1, pv);
        if (Request.Headers.TryGetValue("X-Page-Size", out var ps) && int.TryParse(ps.ToString(), out var psz)) pageSize = Math.Max(1, psz);
        try
        {
            var list = await _repo.GetTasksPagedAsync(page, pageSize);
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
            return Ok(list);
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }

    // GET api/upload/collections/{collectionName}
    // Pagination via headers and optional X-Upload-Id header to filter
    [HttpGet("collections/{collectionName}")]
    public async Task<IActionResult> GetCollectionDocuments([FromRoute] string collectionName)
    {
        if (string.IsNullOrWhiteSpace(collectionName)) return BadRequest("Missing collectionName");

        int page = 1; int pageSize = 50;
        if (Request.Headers.TryGetValue("X-Page", out var p) && int.TryParse(p.ToString(), out var pv)) page = Math.Max(1, pv);
        if (Request.Headers.TryGetValue("X-Page-Size", out var ps) && int.TryParse(ps.ToString(), out var psz)) pageSize = Math.Max(1, psz);

        string? uploadId = null;
        if (Request.Headers.TryGetValue("X-Upload-Id", out var uid)) uploadId = uid.ToString();

        try
        {
            var docs = await _repo.GetDocumentsPagedAsync(collectionName, page, pageSize, uploadId);
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
            return Ok(docs);
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }
}
