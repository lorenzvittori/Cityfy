using ServiceDefault.City;
using ServiceDefault.Contracts;

namespace CityFy.Elaboration.Services;

public interface ICityService
{
    /// <summary>
    /// Aggregates the user's plays, resolves the artists' genres and stores the city.
    /// Throws CITY_INVALID_PERIOD (400) for a malformed period.
    /// </summary>
    Task<CityModel> BuildAsync(string userId, string? period, CancellationToken ct);

    /// <summary>Last built city, null if never built. Throws CITY_INVALID_PERIOD (400) for a malformed period.</summary>
    Task<CityModel?> GetAsync(string userId, string? period, CancellationToken ct);

    /// <summary>Builds the city for every requested period (invalid ones are skipped), then publishes CityBuilt.</summary>
    Task HandleBuildRequestedAsync(BuildCityRequested message, CancellationToken ct);

    /// <summary>Last attempt failed: publishes CityBuildFailed with the error code.</summary>
    Task HandleBuildFailedAsync(BuildCityRequested message, Exception error, CancellationToken ct);
}
