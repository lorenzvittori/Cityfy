using ServiceDefault.City;
using ServiceDefault.Contracts;

namespace CityFy.CityGenerator.Services;

public interface ICityGenerationService
{
    /// <summary>
    /// Validates the request and queues the generation (202). An identical request on the same
    /// catalog returns the existing city. Throws the CITY_INVALID_* codes (400, CITY_INVALID_RECIPE with "problems"),
    /// CITY_CATALOG_MISSING (404), CITY_LIMIT_REACHED (409).
    /// </summary>
    Task<GeneratedCityInfo> RequestAsync(string userId, GenerateCityRequest request, CancellationToken ct);

    /// <summary>Keys, ranges and presets of the parameter string of a custom map.</summary>
    RecipeSchema GetRecipeSchema();

    /// <summary>Defaults and limits of the city form (districts, name, genres).</summary>
    CityFormOptions GetFormOptions();

    Task<GeneratedCityPage> ListAsync(string userId, int page, int pageSize, CancellationToken ct);

    /// <summary>Throws CITY_NOT_FOUND (404), also for a city of another user.</summary>
    Task<GeneratedCityDetail> GetAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>Stops the job if running, then deletes. Throws CITY_NOT_FOUND (404).</summary>
    Task DeleteAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>Throws CITY_NOT_FOUND (404), PROCESS_NOT_CANCELLABLE (409) when already finished.</summary>
    Task<GeneratedCityInfo> CancelAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>
    /// The job (GenerateCityRequested consumer). Throws on failure: the messaging layer retries and
    /// marks the city Failed after the last attempt. A cancellation ends normally.
    /// </summary>
    Task GenerateAsync(string cityId, CancellationToken ct);

    Task MarkFailedAsync(string cityId, Exception error, CancellationToken ct);

    /// <summary>First catalog of a user without cities: queues the default city (whole history, organic, every genre).</summary>
    Task HandleCatalogUpdatedAsync(UserCatalogUpdated message, CancellationToken ct);

    /// <summary>Startup: queues again the cities left Pending or interrupted. Returns how many.</summary>
    Task<int> RequeueUnfinishedAsync(CancellationToken ct);
}
