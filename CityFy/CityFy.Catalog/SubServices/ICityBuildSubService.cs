using CityFy.Catalog.Models;
using ServiceDefault.City;

namespace CityFy.Catalog.SubServices;

/// <summary>Pure transformation: per-artist plays + the user's artist placements -> city (no I/O).</summary>
public interface ICityBuildSubService
{
    /// <summary>
    /// Only the artists kept by <paramref name="filter"/> are in the city and in its totals; with
    /// <see cref="GenreFilter.All"/> the artists without a genre are reported as unresolved.
    /// </summary>
    CityModel Build(
        string userId,
        DateTime? from,
        DateTime? to,
        DateTime now,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName,
        GenreFilter filter);
}
