using ServiceDefault.City;

namespace CityFy.CityGenerator.Clients;

/// <summary>CityFy.Catalog: the data of a city (districts, blocks, buildings) for a date range and genres.</summary>
public interface ICatalogClient
{
    /// <summary>Throws CITY_CATALOG_MISSING (404) before the user's first import.</summary>
    Task<UserCatalogVersion> GetVersionAsync(string userId, CancellationToken ct);

    /// <summary>Throws CITY_INVALID_RANGE (400), CITY_CATALOG_MISSING (404); SERVICE_UNAVAILABLE (503) when Catalog is down.</summary>
    Task<CityModelResult> GetCityModelAsync(string userId, CityModelQuery query, CancellationToken ct);
}
