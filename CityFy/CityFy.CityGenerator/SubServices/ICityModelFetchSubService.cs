using CityFy.CityGenerator.Models;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

/// <summary>City data from CityFy.Catalog.</summary>
public interface ICityModelFetchSubService
{
    /// <summary>Current catalog version of the user; throws CITY_CATALOG_MISSING (404) before the first import.</summary>
    Task<string> GetCatalogVersionAsync(string userId, CancellationToken ct);

    /// <summary>Districts > blocks > buildings for the range and genres of the city.</summary>
    Task<CityModelResult> FetchAsync(GeneratedCity city, CancellationToken ct);
}
