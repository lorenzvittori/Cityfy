using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

/// <summary>Read API over the imported catalog.</summary>
public interface IMusicBrainzQueryService
{
    /// <summary>Spotify ids and/or names -> MusicBrainz artist + genres (by votes). Every input gets an answer.</summary>
    Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct);

    Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct);

    /// <summary>
    /// Top-level ancestors of a genre following "subgenre" edges upwards (a genre can have more
    /// than one parent). A genre without parents is its own root. Null if the genre does not exist.
    /// </summary>
    Task<IReadOnlyList<GenreNode>?> GetGenreRootsAsync(int genreId, CancellationToken ct);
}