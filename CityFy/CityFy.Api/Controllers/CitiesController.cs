using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;

namespace CityFy.Api.Controllers;

/// <summary>
/// Saved cities of the logged-in user (CityGenerator). A city of another user answers 404
/// CITY_NOT_FOUND. The generation runs in background: follow it with GET api/v1/cities/{cityId}.
/// </summary>
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1/cities")]
public sealed class CitiesController(ICityGeneratorClient cities) : ControllerBase
{
    // POST api/v1/cities { name?, from?, to?, layout?, genres?, subgenres? } — 202 with the city (Pending,
    // or the existing one when an identical city is already there)
    [HttpPost]
    public async Task<ActionResult<GeneratedCityDto>> Create([FromBody] CreateCityRequest request, CancellationToken ct)
    {
        var city = (await cities.CreateAsync(User.UserId(), request.ToInternal(), ct)).ToDto();
        return AcceptedAtAction(nameof(Get), new { cityId = city.CityId }, city);
    }

    // GET api/v1/cities/recipe/schema — keys, ranges and presets of the parameter string of a custom map
    // (shared contract, exposed as-is: the form is built from it)
    [HttpGet("recipe/schema")]
    public async Task<ActionResult<RecipeSchema>> RecipeSchema(CancellationToken ct) => Ok(await cities.GetRecipeSchemaAsync(ct));

    // GET api/v1/cities?page=1&pageSize=10 — most recent first
    [HttpGet]
    public async Task<ActionResult<GeneratedCityPageDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        Ok((await cities.ListAsync(User.UserId(), page, pageSize, ct)).ToDto());

    // GET api/v1/cities/{cityId} — status and, when Done, districts > blocks > buildings
    [HttpGet("{cityId}")]
    public async Task<ActionResult<GeneratedCityDetailDto>> Get(string cityId, CancellationToken ct) =>
        Ok((await cities.GetAsync(User.UserId(), cityId, ct)).ToDto());

    // DELETE api/v1/cities/{cityId} — 204 (a running generation is stopped)
    [HttpDelete("{cityId}")]
    public async Task<IActionResult> Delete(string cityId, CancellationToken ct)
    {
        await cities.DeleteAsync(User.UserId(), cityId, ct);
        return NoContent();
    }

    // POST api/v1/cities/{cityId}/cancel — stop a queued or running generation (409 when finished)
    [HttpPost("{cityId}/cancel")]
    public async Task<ActionResult<GeneratedCityDto>> Cancel(string cityId, CancellationToken ct) =>
        Ok((await cities.CancelAsync(User.UserId(), cityId, ct)).ToDto());
}
