using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Repositories;

public interface ILastFmAccountRepository
{
    Task<LastFmAccount?> GetAsync(string userId, CancellationToken ct);

    Task<LastFmAccount?> GetByUsernameAsync(string username, CancellationToken ct);

    Task<IReadOnlyList<LastFmAccount>> ListAsync(CancellationToken ct);

    /// <summary>Insert or replace (a new link of the same user replaces session key and cursors).</summary>
    Task UpsertAsync(LastFmAccount account, CancellationToken ct);

    /// <summary>Active accounts with NextPollAt &lt;= now, oldest first.</summary>
    Task<IReadOnlyList<LastFmAccount>> ListDueAsync(DateTime now, int limit, CancellationToken ct);

    /// <summary>After a poll: cursor (only moves forward), activity and next poll.</summary>
    Task UpdatePollAsync(string userId, DateTime polledAt, DateTime lastScrobbleAt, DateTime? lastListeningAt, DateTime nextPollAt, CancellationToken ct);

    Task SuspendAsync(string userId, string errorCode, CancellationToken ct);

    Task SetNextPollAsync(string userId, DateTime nextPollAt, CancellationToken ct);

    /// <summary>Moves the next poll earlier, to <paramref name="at"/>, never later.</summary>
    Task PullNextPollAsync(string userId, DateTime at, CancellationToken ct);

    Task SetHistoryImportAsync(string userId, LastFmHistoryImport? import, CancellationToken ct);

    Task<long> DeleteAsync(string userId, CancellationToken ct);

    /// <summary>Every account (end of the Last.fm licence).</summary>
    Task<long> DeleteAllAsync(CancellationToken ct);
}
