using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Retrieve-MusicBrainz")]
[Route("api/genres")]
public class GenresController : ControllerBase
{
    private readonly MusicBrainzQueryService _query;

    public GenresController(MusicBrainzQueryService query)
    {
        _query = query;
    }

    // GET api/genres/graph — all genres and their relations (subgenre, influenced by, fusion of)
    [HttpGet("graph")]
    public async Task<ActionResult<GenreGraph>> GetGraph(CancellationToken ct) =>
        Ok(await _query.GetGenreGraphAsync(ct));

    // GET api/genres/{id}/roots — top-level ancestors following the subgenre hierarchy
    [HttpGet("{id:int}/roots")]
    public async Task<ActionResult<IReadOnlyList<GenreNode>>> GetRoots(int id, CancellationToken ct)
    {
        var roots = await _query.GetGenreRootsAsync(id, ct);
        return roots == null
            ? this.CityFyProblem(ErrorCodes.MbGenreNotFound, $"Genre {id} not found", StatusCodes.Status404NotFound)
            : Ok(roots);
    }
}
