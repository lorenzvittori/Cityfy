using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Api.Controllers;

/// <summary>
/// Data of the logged-in user behind the cities: genres available for a new city and the refresh of
/// the artist catalog. The cities themselves are in <see cref="CitiesController"/>.
/// </summary>
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/city")]
public sealed class CityController(ICatalogClient catalog, IOrchestratorClient orchestrator) : ControllerBase
{
    // POST api/v1/city/rebuild — refresh the user's artist catalog now from the plays already imported:
    // useful when the genre catalog changed or after combining sources. Follow it with GET api/v1/pipeline/runs.
    [HttpPost("rebuild")]
    public async Task<ActionResult<PipelineRunDto>> Rebuild(CancellationToken ct) =>
        Accepted((await orchestrator.TriggerAsync(User.UserId(), ct)).ToDto());

    // GET api/v1/city/genres?from=&to= — genres and subgenres listened in the range, to choose those of a
    // new city (404 CITY_CATALOG_MISSING while the first import is running)
    [HttpGet("genres")]
    public async Task<ActionResult<IReadOnlyList<GenreOptionDto>>> GetGenreOptions([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        Ok((await catalog.GetGenresAsync(User.UserId(), from, to, ct)).Select(g => g.ToDto()).ToList());
}
