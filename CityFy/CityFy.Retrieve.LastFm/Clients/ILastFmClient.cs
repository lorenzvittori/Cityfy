using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Clients;

/// <summary>
/// Last.fm API. Every request passes through <see cref="ILastFmRequestGate"/>; rate limits and
/// temporary errors are retried (MaxAttempts). Throws <see cref="LastFmApiException"/> with the
/// Last.fm code when the call ultimately fails, <see cref="HttpRequestException"/> when Last.fm
/// cannot be reached.
/// </summary>
public interface ILastFmClient
{
    /// <summary>Authorization page of Last.fm; Last.fm comes back to <paramref name="callbackUrl"/>?token=...</summary>
    string GetAuthorizeUrl(string callbackUrl);

    /// <summary>auth.getSession: the token (valid 60 minutes, single use) → user name and session key.</summary>
    Task<LastFmSession> GetSessionAsync(string token, CancellationToken ct);

    /// <summary>
    /// user.getRecentTracks with extended=1, newest first. <paramref name="from"/> and <paramref name="to"/>
    /// are exclusive bounds. Signed with the session key when given (needed for private profiles).
    /// </summary>
    Task<LastFmRecentTracksPage> GetRecentTracksAsync(
        string username, string? sessionKey, DateTime? from, DateTime? to, int page, CancellationToken ct);
}
