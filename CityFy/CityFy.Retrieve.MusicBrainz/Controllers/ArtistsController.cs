using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.AspNetCore.Mvc;

namespace CityFy.Retrieve.MusicBrainz.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "Retrieve-MusicBrainz")]
[Route("api/artists")]
public sealed class ArtistsController(IMusicBrainzQueryService query) : ControllerBase
{
    // POST api/artists/resolve
    // Spotify ids and/or names -> MusicBrainz artist + genres (ordered by votes).
    // Every input gets an answer, with matchType "none" when not found. 400 MB_TOO_MANY_ITEMS beyond the limit.
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveArtistsResponse>> Resolve([FromBody] ResolveArtistsRequest request, CancellationToken ct) =>
        Ok(await query.ResolveArtistsAsync(request, ct));
}