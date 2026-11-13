using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>Web authorization of Last.fm.</summary>
public interface ILastFmAuthSubService
{
    /// <summary>Page where the user authorizes CityFy; Last.fm comes back to LastFm:CallbackUrl?token=...</summary>
    string GetAuthorizeUrl();

    /// <summary>
    /// Token → verified user name and session key. Throws LASTFM_AUTH_FAILED (400) for a token refused,
    /// expired or already used; LASTFM_UNAVAILABLE (502) when Last.fm does not answer.
    /// </summary>
    Task<LastFmSession> ExchangeTokenAsync(string token, CancellationToken ct);
}
