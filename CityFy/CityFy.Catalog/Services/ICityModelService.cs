using ServiceDefault.City;

namespace CityFy.Catalog.Services;

/// <summary>On request: the city of any date range and genres, computed from the stored catalog (nothing is saved).</summary>
public interface ICityModelService
{
    /// <summary>
    /// Throws CITY_INVALID_RANGE (400) for a bad range, CITY_CATALOG_MISSING (404) when the user's
    /// catalog was never built.
    /// </summary>
    Task<CityModelResult> GetAsync(string userId, CityModelQuery query, CancellationToken ct);

    /// <summary>Current catalog version of the user. Throws CITY_CATALOG_MISSING (404) when it was never built.</summary>
    Task<UserCatalogVersion> GetCatalogVersionAsync(string userId, CancellationToken ct);

    /// <summary>Genres and subgenres listened in the range. Same errors as <see cref="GetAsync"/>.</summary>
    Task<IReadOnlyList<GenreOption>> GetGenresAsync(string userId, DateTime? from, DateTime? to, CancellationToken ct);
}
