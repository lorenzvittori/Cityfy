using System.Net;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class RetrieveErrorSubService : IRetrieveErrorSubService
{
    public string ToErrorCode(Exception error) => error switch
    {
        CityFyException c => c.Code,
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => ErrorCodes.SpotifyRateLimited,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => ErrorCodes.SpotifyReauthRequired,
        HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError } => ErrorCodes.SpotifyUnavailable,
        TaskCanceledException => ErrorCodes.SpotifyUnavailable, // HTTP timeout
        _ => ErrorCodes.SpotifyRetrieveFailed
    };
}