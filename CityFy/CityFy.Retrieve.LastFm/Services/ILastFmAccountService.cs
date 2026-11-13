using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Services;

public interface ILastFmAccountService
{
    string GetAuthorizeUrl();

    /// <summary>
    /// Links the Last.fm account authorized with <paramref name="token"/> to <paramref name="userId"/>
    /// (a new user) or to its existing owner, whose id is in the answer; the first time, starts the
    /// import of the whole history. Throws LASTFM_AUTH_FAILED, LASTFM_UNAVAILABLE.
    /// </summary>
    Task<LinkResponse> LinkAsync(string userId, string token, CancellationToken ct);

    /// <summary>Throws LASTFM_NOT_LINKED.</summary>
    Task<LastFmAccountResponse> GetAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<LastFmAccountResponse>> ListAsync(CancellationToken ct);

    /// <summary>Imports the whole history again (stops an import still running). Throws LASTFM_NOT_LINKED.</summary>
    Task<string> StartImportAsync(string userId, CancellationToken ct);

    /// <summary>Reads the account at the next tick of the polling. Throws LASTFM_NOT_LINKED.</summary>
    Task PollNowAsync(string userId, CancellationToken ct);

    /// <summary>
    /// UserActive: the user has a CityFy page open. Read soon, then at the fast cadence while active.
    /// Nothing happens for a user without a Last.fm account.
    /// </summary>
    Task MarkActiveAsync(string userId, DateTime at, CancellationToken ct);
}
