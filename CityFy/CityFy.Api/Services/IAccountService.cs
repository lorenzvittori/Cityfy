using CityFy.Api.Models;

namespace CityFy.Api.Services;

/// <summary>CityFy accounts: login with Last.fm and the move of the data of an anonymous session.</summary>
public interface IAccountService
{
    /// <summary>Last.fm page where the user authorizes CityFy.</summary>
    Task<string> GetLastFmLoginUrlAsync(CancellationToken ct);

    /// <summary>
    /// Token of the Last.fm callback → CityFy account (created the first time; the same Last.fm account
    /// always gives the same user). With <paramref name="anonymousSessionId"/> the data of that session
    /// moves to the account: when a service does not answer the login still succeeds and the move is
    /// retried in the background. Throws LASTFM_AUTH_FAILED, LASTFM_UNAVAILABLE.
    /// </summary>
    Task<AccountLoginResult> LoginWithLastFmAsync(string token, string? anonymousSessionId, CancellationToken ct);

    /// <summary>The account still exists (its data was not deleted).</summary>
    Task<bool> ExistsAsync(string userId, CancellationToken ct);

    /// <summary>Retries the moves of anonymous sessions left incomplete. Returns how many completed.</summary>
    Task<int> RetryTransfersAsync(CancellationToken ct);
}
