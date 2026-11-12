using CityFy.Elaboration.Models;
using CityFy.Elaboration.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Elaboration.Controllers;

[ApiController]
[Route("debug/elaboration")]
public sealed class DebugElaborationController(ICityService cities) : ControllerBase
{
    private const int MaxUnresolvedShown = 10;

    // POST debug/elaboration/city/{userId}?period=30d
    // Builds the city synchronously and returns its summary. In F2 the orchestrator triggers it.
    [HttpPost("city/{userId}")]
    public async Task<ActionResult<CityBuildSummary>> Build(string userId, [FromQuery] string? period, CancellationToken ct)
    {
        if (!Periods.TryNormalize(period, out var p))
            return this.CityFyProblem(ErrorCodes.CityInvalidPeriod, "period must be 'all' or <n>d|m|y, e.g. 30d, 6m, 1y", StatusCodes.Status400BadRequest);

        var city = await cities.BuildAsync(userId, p, ct);
        return Ok(new CityBuildSummary(city.Id, city.GeneratedAt, city.Summary, city.Districts.Count,
            city.Unresolved.Take(MaxUnresolvedShown).ToList()));
    }
}
