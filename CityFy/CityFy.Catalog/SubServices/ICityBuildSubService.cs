using CityFy.Catalog.Models;
using ServiceDefault.City;

namespace CityFy.Catalog.SubServices;

/// <summary>Pure transformation: per-artist plays + the user's artist placements -> city (no I/O).</summary>
public interface ICityBuildSubService
{
    /// <summary>
    /// The district limit of a request (null = the configured default, merge).
    /// Throws CITY_INVALID_DISTRICTS (400).
    /// </summary>
    DistrictLimit Limit(int? maxDistricts, string? extraGenres);

    /// <summary>
    /// Only the artists kept by <paramref name="filter"/> are in the city and in its totals; with
    /// <see cref="GenreFilter.All"/> the artists without a genre are reported as unresolved.
    /// Beyond <paramref name="limit"/> the least listened macro-genres are merged into "Other genres"
    /// or dropped (dropped = left out like the filter does).
    /// </summary>
    CityModel Build(
        string userId,
        DateTime? from,
        DateTime? to,
        DateTime now,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName,
        GenreFilter filter,
        DistrictLimit limit);
}
