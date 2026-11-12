using CityFy.Catalog.Models;

namespace CityFy.Catalog.SubServices;

public interface IArtistResolveSubService
{
    /// <summary>
    /// MusicBrainz artist (with genres) of each played artist, keyed by the artist name of the plays.
    /// Spotify id match first (reliable), name match as fallback (export plays have no artist id).
    /// Artists without a match are missing from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, ResolvedArtist>> ResolveAsync(IReadOnlyList<ArtistPlays> plays, CancellationToken ct);
}
