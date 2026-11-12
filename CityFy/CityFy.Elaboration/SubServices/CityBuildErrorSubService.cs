using ServiceDefault.Errors;

namespace CityFy.Elaboration.SubServices;

public sealed class CityBuildErrorSubService : ICityBuildErrorSubService
{
    public string ToErrorCode(Exception error) => error switch
    {
        CityFyException c => c.Code,
        HttpRequestException or TaskCanceledException => ErrorCodes.MusicBrainzUnavailable,
        Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException => ErrorCodes.MusicBrainzUnavailable,
        _ => ErrorCodes.CityBuildFailed
    };
}
