using CityFy.Retrieve.LastFm.Models;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmErrorSubService : ILastFmErrorSubService
{
    public string ToErrorCode(Exception error, string fallback) => error switch
    {
        CityFyException c => c.Code,
        LastFmApiException { Code: LastFmApiException.RateLimitExceeded } => ErrorCodes.LastFmRateLimited,
        LastFmApiException { Code: LastFmApiException.AuthenticationFailed or LastFmApiException.InvalidSessionKey or LastFmApiException.InvalidParameters }
            => ErrorCodes.LastFmAuthFailed,
        LastFmApiException { Code: LastFmApiException.LoginRequired } => ErrorCodes.LastFmUserPrivate,
        LastFmApiException => ErrorCodes.LastFmUnavailable,
        HttpRequestException or TaskCanceledException or InvalidDataException => ErrorCodes.LastFmUnavailable,
        _ => fallback
    };

    public bool IsAccountProblem(string errorCode) => errorCode is ErrorCodes.LastFmAuthFailed or ErrorCodes.LastFmUserPrivate;

    public bool IsConfigurationProblem(Exception error) =>
        error is LastFmApiException { Code: LastFmApiException.InvalidApiKey or LastFmApiException.SuspendedApiKey };
}
