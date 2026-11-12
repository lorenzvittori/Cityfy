using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.SubServices;

public interface IArtistResolveSubService
{
    /// <summary>
    /// Spotify ids and/or names -> MusicBrainz artist + genres (ordered by votes). Every input gets
    /// an answer, with matchType "none" when not found. Throws MB_TOO_MANY_ITEMS (400) beyond the limit.
    /// </summary>
    Task<ResolveArtistsResponse> ResolveAsync(ResolveArtistsRequest request, CancellationToken ct);
}