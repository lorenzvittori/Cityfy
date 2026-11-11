using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.AspNetCore.Mvc;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Retrieve-MusicBrainz")]
[Route("api/artists")]
public class ArtistsController : ControllerBase
{
    private readonly MusicBrainzQueryService _query;

    public ArtistsController(MusicBrainzQueryService query)
    {
        _query = query;
    }

    // POST api/artists/resolve
    // Spotify ids and/or names -> MusicBrainz artist + genres (ordered by votes).
    // Every input gets an answer, with matchType "none" when not found.
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveArtistsResponse>> Resolve([FromBody] ResolveArtistsRequest request, CancellationToken ct)
    {
        if ((request.SpotifyArtistIds?.Count ?? 0) > MusicBrainzQueryService.MaxItemsPerRequest ||
            (request.Names?.Count ?? 0) > MusicBrainzQueryService.MaxItemsPerRequest)
            return this.CityFyProblem(ErrorCodes.MbTooManyItems,
                $"At most {MusicBrainzQueryService.MaxItemsPerRequest} spotifyArtistIds and {MusicBrainzQueryService.MaxItemsPerRequest} names per request",
                StatusCodes.Status400BadRequest);

        return Ok(await _query.ResolveArtistsAsync(request, ct));
    }
}
