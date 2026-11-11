using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>
/// City of the logged-in user. period: "all" (default), "1y", "6m", "30d" (any &lt;n&gt;d|m|y).
/// Never built -> 404 CITY_NOT_READY: check GET api/v1/pipeline/runs to tell "in progress" from "no data".
/// </summary>
[ApiController]
[Authorize]
[ApiExplorerSettings(GroupName = "v1")]
[Route("api/v1")]
public class CityController : ControllerBase
{
    private readonly ElaborationClient _elaboration;

    public CityController(ElaborationClient elaboration)
    {
        _elaboration = elaboration;
    }

    private Task<CityModel> CityAsync(string? period, CancellationToken ct) =>
        _elaboration.GetCityAsync(User.UserId(), string.IsNullOrWhiteSpace(period) ? "all" : period, ct);

    // GET api/v1/city?period=30d — the whole city (districts > blocks > buildings)
    [HttpGet("city")]
    public async Task<ActionResult<CityDto>> GetCity([FromQuery] string? period, CancellationToken ct) =>
        Ok((await CityAsync(period, ct)).ToDto());

    // GET api/v1/districts?period=30d
    [HttpGet("districts")]
    public async Task<ActionResult<IReadOnlyList<DistrictSummaryDto>>> GetDistricts([FromQuery] string? period, CancellationToken ct) =>
        Ok((await CityAsync(period, ct)).Districts.Select(d => d.ToSummaryDto()).ToList());

    // GET api/v1/districts/{districtId}?period=30d
    [HttpGet("districts/{districtId}")]
    public async Task<ActionResult<DistrictDto>> GetDistrict(string districtId, [FromQuery] string? period, CancellationToken ct)
    {
        var district = (await CityAsync(period, ct)).Districts.FirstOrDefault(d => d.DistrictId == districtId);
        return district == null ? NotFoundProblem($"District '{districtId}'") : Ok(district.ToDto());
    }

    // GET api/v1/genres?period=30d — every genre (block) of the city, most listened first
    [HttpGet("genres")]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetGenres([FromQuery] string? period, CancellationToken ct) =>
        Ok((await CityAsync(period, ct)).Districts
            .SelectMany(d => d.Blocks.Select(b => b.ToGenreDto(d)))
            .OrderByDescending(g => g.Score)
            .ToList());

    // GET api/v1/genres/{genreId}?period=30d
    [HttpGet("genres/{genreId:int}")]
    public async Task<ActionResult<GenreDetailDto>> GetGenre(int genreId, [FromQuery] string? period, CancellationToken ct)
    {
        var city = await CityAsync(period, ct);
        var match = city.Districts.SelectMany(d => d.Blocks.Select(b => (District: d, Block: b)))
            .FirstOrDefault(x => x.Block.GenreId == genreId);

        return match.Block == null
            ? NotFoundProblem($"Genre {genreId}")
            : Ok(new GenreDetailDto(match.Block.ToGenreDto(match.District), match.Block.Buildings.Select(b => b.ToDto()).ToList()));
    }

    // GET api/v1/artists/{artistId}?period=30d — artistId = MusicBrainz MBID (BuildingDto.ArtistId)
    [HttpGet("artists/{artistId}")]
    public async Task<ActionResult<ArtistDetailDto>> GetArtist(string artistId, [FromQuery] string? period, CancellationToken ct)
    {
        var city = await CityAsync(period, ct);
        var match = city.Districts
            .SelectMany(d => d.Blocks.SelectMany(b => b.Buildings.Select(x => (District: d, Block: b, Building: x))))
            .FirstOrDefault(x => x.Building.BuildingId == artistId);

        return match.Building == null
            ? NotFoundProblem($"Artist '{artistId}'")
            : Ok(new ArtistDetailDto(match.Building.ToDto(), match.District.DistrictId, match.District.Name, match.Block.GenreId, match.Block.Name));
    }

    private ObjectResult NotFoundProblem(string what) =>
        this.CityFyProblem(ErrorCodes.NotFound, $"{what} is not in this city", StatusCodes.Status404NotFound);
}
