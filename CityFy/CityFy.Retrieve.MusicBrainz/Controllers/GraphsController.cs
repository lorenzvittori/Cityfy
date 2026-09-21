using CityFy.Retrieve.MusicBrainz.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Retrieve-MusicBrainz")]
[Route("api/musicbrainz/graphs")]
public class GraphsController : ControllerBase
{
    private readonly IMusicBrainzRepository _repo;

    public GraphsController(IMusicBrainzRepository repo)
    {
        _repo = repo;
    }

    // GET api/musicbrainz/graphs/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("Missing id");
        try
        {
            var graph = await _repo.GetTagGraphByIdAsync(id);
            if (graph == null) return NotFound();
            return Ok(graph);
        }
        catch (FormatException)
        {
            return BadRequest("Invalid id format");
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }

    // GET api/musicbrainz/graphs
    // Pagination parameters are read from headers: X-Page, X-Page-Size
    [HttpGet]
    public async Task<IActionResult> GetPaged()
    {
        int page = 1;
        int pageSize = 20;

        if (Request.Headers.TryGetValue("X-Page", out var p) && int.TryParse(p.ToString(), out var pv)) page = Math.Max(1, pv);
        if (Request.Headers.TryGetValue("X-Page-Size", out var ps) && int.TryParse(ps.ToString(), out var psz)) pageSize = Math.Max(1, psz);

        try
        {
            var list = await _repo.GetTagGraphsPagedAsync(page, pageSize);
            // Include pagination info in headers
            Response.Headers["X-Page"] = page.ToString();
            Response.Headers["X-Page-Size"] = pageSize.ToString();
            return Ok(list);
        }
        catch (Exception)
        {
            return StatusCode(500);
        }
    }
}
