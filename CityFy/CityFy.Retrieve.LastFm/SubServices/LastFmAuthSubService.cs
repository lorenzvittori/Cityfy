using CityFy.Retrieve.LastFm.Clients;
using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmAuthSubService(ILastFmClient client, IOptions<LastFmOptions> options) : ILastFmAuthSubService
{
    public string GetAuthorizeUrl() => client.GetAuthorizeUrl(options.Value.CallbackUrl);

    public async Task<LastFmSession> ExchangeTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new CityFyException(ErrorCodes.LastFmAuthFailed, "Missing Last.fm token", StatusCodes.Status400BadRequest);
        try
        {
            return await client.GetSessionAsync(token, ct);
        }
        // 4 invalid token, 14 not authorized yet, 15 expired: the user has to authorize again.
        // 10/26 are about the CityFy API key, not the user's token.
        catch (LastFmApiException ex) when (!ex.IsTransient && ex.Code is not (LastFmApiException.InvalidApiKey or LastFmApiException.SuspendedApiKey))
        {
            throw new CityFyException(ErrorCodes.LastFmAuthFailed, $"Last.fm refused the token: {ex.Message}", StatusCodes.Status400BadRequest);
        }
        catch (Exception ex) when (ex is LastFmApiException or HttpRequestException or InvalidDataException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new CityFyException(ErrorCodes.LastFmUnavailable, $"Last.fm session for the token failed: {ex.Message}", StatusCodes.Status502BadGateway);
        }
    }
}
