using CityFy.CityGenerator.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Controllers;

/// <summary>
/// Generated cities of a user (called by the gateway, which authenticates the user and passes the id).
/// A city of another user answers 404 CITY_NOT_FOUND.
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "CityGenerator")]
[Route("api/city-generator/{userId}/cities")]
public sealed class CitiesController(ICityGenerationService cities) : ControllerBase
{
    // POST api/city-generator/{userId}/cities { name?, from?, to?, layout?, recipe?, genres?, subgenres?, maxDistricts?, extraGenres? } — queue a generation
    [HttpPost]
    public async Task<ActionResult<GeneratedCityInfo>> Create(string userId, [FromBody] GenerateCityRequest request, CancellationToken ct)
    {
        var city = await cities.RequestAsync(userId, request, ct);
        return AcceptedAtAction(nameof(Get), new { userId, cityId = city.CityId }, city);
    }

    // GET api/city-generator/{userId}/cities?page=1&pageSize=10 — most recent first, without content
    [HttpGet]
    public async Task<ActionResult<GeneratedCityPage>> List(string userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        Ok(await cities.ListAsync(userId, page, pageSize, ct));

    // GET api/city-generator/{userId}/cities/{cityId} — status and, when Done, the city
    [HttpGet("{cityId}")]
    public async Task<ActionResult<GeneratedCityDetail>> Get(string userId, string cityId, CancellationToken ct) =>
        Ok(await cities.GetAsync(userId, cityId, ct));

    // DELETE api/city-generator/{userId}/cities/{cityId}
    [HttpDelete("{cityId}")]
    public async Task<IActionResult> Delete(string userId, string cityId, CancellationToken ct)
    {
        await cities.DeleteAsync(userId, cityId, ct);
        return NoContent();
    }

    // POST api/city-generator/{userId}/cities/{cityId}/cancel — stop a queued or running generation
    [HttpPost("{cityId}/cancel")]
    public async Task<ActionResult<GeneratedCityInfo>> Cancel(string userId, string cityId, CancellationToken ct) =>
        Ok(await cities.CancelAsync(userId, cityId, ct));
}
