using CityFy.CityGenerator.Models;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

/// <summary>Pure: validation and canonical hash of a generation request.</summary>
public interface ICityRequestSubService
{
    /// <summary>
    /// Trimmed name (empty = null), UTC dates, lower-case layout (default organic), genre ids sorted
    /// and distinct, Recipe kept only for "custom" (raw: see ILayoutRecipeParseSubService). Throws CITY_INVALID_NAME, CITY_INVALID_RANGE, CITY_INVALID_LAYOUT, CITY_INVALID_GENRES (400).
    /// </summary>
    CityRequest Validate(GenerateCityRequest request, DateTime now);

    /// <summary>Same parameters (whatever the order of the genres, the name excluded) = same hash.</summary>
    string Hash(CityRequest request);
}
