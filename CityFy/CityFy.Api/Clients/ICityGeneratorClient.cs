using ServiceDefault.City;

namespace CityFy.Api.Clients;

/// <summary>CityGenerator: the saved cities of a user, plus all of them on data deletion.</summary>
public interface ICityGeneratorClient : IUserDataOwner
{
    /// <summary>Throws the CITY_INVALID_* codes (400), CITY_CATALOG_MISSING (404), CITY_LIMIT_REACHED (409).</summary>
    Task<GeneratedCityInfo> CreateAsync(string userId, GenerateCityRequest request, CancellationToken ct);

    /// <summary>Keys, ranges and presets of the parameter string of a custom map.</summary>
    Task<RecipeSchema> GetRecipeSchemaAsync(CancellationToken ct);

    /// <summary>Defaults and limits of the city form (districts, name, genres).</summary>
    Task<CityFormOptions> GetFormOptionsAsync(CancellationToken ct);

    Task<GeneratedCityPage> ListAsync(string userId, int page, int pageSize, CancellationToken ct);

    /// <summary>Throws CITY_NOT_FOUND (404), also for a city of another user.</summary>
    Task<GeneratedCityDetail> GetAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>Throws CITY_NOT_FOUND (404).</summary>
    Task DeleteAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>Throws CITY_NOT_FOUND (404), PROCESS_NOT_CANCELLABLE (409).</summary>
    Task<GeneratedCityInfo> CancelAsync(string userId, string cityId, CancellationToken ct);
}
