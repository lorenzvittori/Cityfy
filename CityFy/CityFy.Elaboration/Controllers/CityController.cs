using ServiceDefault.City;
using CityFy.Elaboration.Models;
using CityFy.Elaboration.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Elaboration.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Elaboration")]
[Route("api/elaboration/city")]
public class CityController : ControllerBase
{
    private readonly ICityService _cities;

    public CityController(ICityService cities)
    {
        _cities = cities;
    }

    // GET api/elaboration/city/{userId}?period=30d — last built city (404 if never built)
    [HttpGet("{userId}")]
    public async Task<ActionResult<CityModel>> Get(string userId, [FromQuery] string? period, CancellationToken ct)
    {
        if (!Periods.TryNormalize(period, out var p))
            return this.CityFyProblem(ErrorCodes.CityInvalidPeriod, "period must be 'all' or <n>d|m|y, e.g. 30d, 6m, 1y", StatusCodes.Status400BadRequest);

        // Not built yet: the frontend checks the pipeline runs to tell "in progress" from "no data"
        var city = await _cities.GetAsync(userId, p, ct);
        return city == null
            ? this.CityFyProblem(ErrorCodes.CityNotReady, $"No city built for user '{userId}' and period '{p}'", StatusCodes.Status404NotFound)
            : Ok(city);
    }
}

/// <summary>Service-to-service endpoint used by CityFy.Api when a user (or the admin) deletes the user's data.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "Internal")]
[Route("internal/users")]
public class InternalUserDataController : ControllerBase
{
    // DELETE internal/users/{userId}/data — every play (all sources) and every city of the user
    [HttpDelete("{userId}/data")]
    public async Task<ActionResult<UserDataDeletion>> Delete(string userId,
        [FromServices] CityFy.Elaboration.Repositories.IElaborationRepository repo, CancellationToken ct)
    {
        var (plays, cities) = await repo.DeleteUserDataAsync(userId, ct);
        return Ok(new UserDataDeletion("Elaboration", new Dictionary<string, long> { ["plays"] = plays, ["cities"] = cities }));
    }
}
