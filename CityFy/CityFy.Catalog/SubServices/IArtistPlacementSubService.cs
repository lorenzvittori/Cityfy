using CityFy.Catalog.Models;

namespace CityFy.Catalog.SubServices;

/// <summary>Pure: places every played artist in a district (top-level genre) and a block (genre).</summary>
public interface IArtistPlacementSubService
{
    /// <summary>
    /// One placement per artist of the plays, for the given catalog version. Artists without a
    /// MusicBrainz match or without genres are returned unplaced.
    /// </summary>
    IReadOnlyList<ArtistPlacement> Place(
        string userId,
        string catalogVersion,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ResolvedArtist> resolvedByArtistName,
        GenreHierarchy hierarchy);
}
