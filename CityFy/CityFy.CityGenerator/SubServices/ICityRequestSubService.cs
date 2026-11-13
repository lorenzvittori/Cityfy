using CityFy.CityGenerator.Models;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

/// <summary>Pure: validation and canonical hash of a generation request.</summary>
public interface ICityRequestSubService
{
    /// <summary>
    /// Trimmed name (empty = null), UTC dates, lower-case layout (default organic), genre ids sorted
    /// and distinct, Recipe kept only for "custom" (raw: see ILayoutRecipeParseSubService), district
    /// limit with the configured default (null = default, merge).
    /// Throws CITY_INVALID_NAME, CITY_INVALID_RANGE, CITY_INVALID_LAYOUT, CITY_INVALID_GENRES, CITY_INVALID_DISTRICTS (400).
    /// </summary>
    CityRequest Validate(GenerateCityRequest request, DateTime now);

    /// <summary>The first city of a new user: whole history, organic, every genre, default districts.</summary>
    CityRequest Default();

    /// <summary>Defaults and limits for the city form.</summary>
    CityFormOptions FormOptions();

    /// <summary>
    /// Same parameters (whatever the order of the genres, the name excluded) = same hash. The
    /// district limit is hashed after its default is applied: omitted and explicit default are equal.
    /// </summary>
    string Hash(CityRequest request);
}
