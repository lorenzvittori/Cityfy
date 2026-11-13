using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>Linked accounts: link rules, encrypted session key, polling bookkeeping.</summary>
public interface ILastFmAccountStoreSubService
{
    /// <summary>
    /// Links <paramref name="session"/> to <paramref name="userId"/>, or to the user who already owns
    /// that Last.fm account (the Last.fm account is the identity of a login: the returned account
    /// carries the actual user id). A new account starts its polling cursor at <paramref name="now"/>;
    /// a link again keeps the cursor and resumes the polling.
    /// </summary>
    Task<LastFmAccount> LinkAsync(string userId, LastFmSession session, DateTime now, CancellationToken ct);

    Task<LastFmAccount?> GetAsync(string userId, CancellationToken ct);

    /// <summary>Throws LASTFM_NOT_LINKED (404) when the user has no account.</summary>
    Task<LastFmAccount> GetRequiredAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<LastFmAccount>> ListAsync(CancellationToken ct);

    Task<IReadOnlyList<LastFmAccount>> ListDueAsync(DateTime now, CancellationToken ct);

    /// <summary>Decrypted session key. Throws LASTFM_AUTH_FAILED when it can no longer be decrypted (lost key ring).</summary>
    string GetSessionKey(LastFmAccount account);

    Task UpdatePollAsync(string userId, DateTime polledAt, DateTime lastScrobbleAt, DateTime? lastListeningAt, DateTime nextPollAt, CancellationToken ct);

    Task SuspendAsync(string userId, string errorCode, CancellationToken ct);

    Task SetNextPollAsync(string userId, DateTime nextPollAt, CancellationToken ct);

    /// <summary>Next poll at <paramref name="at"/> at the latest.</summary>
    Task PullNextPollAsync(string userId, DateTime at, CancellationToken ct);

    Task SetHistoryImportAsync(string userId, LastFmHistoryImport? import, CancellationToken ct);

    Task<long> DeleteAsync(string userId, CancellationToken ct);

    /// <summary>Every linked account, session keys included.</summary>
    Task<long> DeleteAllAsync(CancellationToken ct);
}
