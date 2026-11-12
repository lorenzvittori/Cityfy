using CityFy.Elaboration.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.Elaboration.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Elaboration")]
[Route("api/elaboration/city")]
public sealed class CityController(ICityService cities) : ControllerBase
{
    // GET api/elaboration/city/{userId}?period=30d — last built city (404 if never built)
    [HttpGet("{userId}")]
    public async Task<ActionResult<CityModel>> Get(string userId, [FromQuery] string? period, CancellationToken ct)
    {
        if (!Periods.TryNormalize(period, out var p))
            return this.CityFyProblem(ErrorCodes.CityInvalidPeriod, "period must be 'all' or <n>d|m|y, e.g. 30d, 6m, 1y", StatusCodes.Status400BadRequest);

        // Not built yet: the frontend checks the pipeline runs to tell "in progress" from "no data"
        var city = await cities.GetAsync(userId, p, ct);
        return city == null
            ? this.CityFyProblem(ErrorCodes.CityNotReady, $"No city built for user '{userId}' and period '{p}'", StatusCodes.Status404NotFound)
            : Ok(city);
    }
}
