using CityFy.Elaboration.Models;
using CityFy.Elaboration.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Elaboration.Controllers;

[ApiController]
[Route("debug/elaboration")]
public sealed class DebugElaborationController(ICityService cities) : ControllerBase
{
    private const int MaxUnresolvedShown = 10;

    // POST debug/elaboration/city/{userId}?period=30d
    // Builds the city synchronously and returns its summary (400 CITY_INVALID_PERIOD for a bad period).
    [HttpPost("city/{userId}")]
    public async Task<ActionResult<CityBuildSummary>> Build(string userId, [FromQuery] string? period, CancellationToken ct)
    {
        var city = await cities.BuildAsync(userId, period, ct);
        return Ok(new CityBuildSummary(city.Id, city.GeneratedAt, city.Summary, city.Districts.Count,
            city.Unresolved.Take(MaxUnresolvedShown).ToList()));
    }
}
