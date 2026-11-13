using ServiceDefault.Errors;

namespace CityFy.Catalog.SubServices;

public sealed class CatalogRefreshErrorSubService : ICatalogRefreshErrorSubService
{
    public string ToErrorCode(Exception error) => error switch
    {
        CityFyException c => c.Code,
        HttpRequestException or TaskCanceledException => ErrorCodes.MusicBrainzUnavailable,
        Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException => ErrorCodes.MusicBrainzUnavailable,
        _ => ErrorCodes.CityBuildFailed
    };
}
