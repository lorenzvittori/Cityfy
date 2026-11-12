using CityFy.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;

namespace CityFy.Catalog.Controllers;

/// <summary>
/// City data of any date range and genres, computed on request from the user's artist catalog
/// (used by the city generator and the gateway; nothing is stored).
/// 400 CITY_INVALID_RANGE for a bad range, 404 CITY_CATALOG_MISSING before the first import.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Catalog")]
[Route("api/catalog")]
public sealed class CityModelController(ICityModelService cityModels) : ControllerBase
{
    // POST api/catalog/city-model/{userId}  { from?, to?, genres?, subgenres? } — districts > blocks > buildings
    [HttpPost("city-model/{userId}")]
    public async Task<ActionResult<CityModelResult>> GetCityModel(string userId, [FromBody] CityModelQuery query, CancellationToken ct) =>
        Ok(await cityModels.GetAsync(userId, query, ct));

    // GET api/catalog/version/{userId} — current catalog version (cheap: lets callers reuse results computed on it)
    [HttpGet("version/{userId}")]
    public async Task<ActionResult<UserCatalogVersion>> GetVersion(string userId, CancellationToken ct) =>
        Ok(await cityModels.GetCatalogVersionAsync(userId, ct));

    // GET api/catalog/genres/{userId}?from=&to= — genres and subgenres listened in the range, most listened first
    [HttpGet("genres/{userId}")]
    public async Task<ActionResult<IReadOnlyList<GenreOption>>> GetGenres(string userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        Ok(await cityModels.GetGenresAsync(userId, from, to, ct));
}
