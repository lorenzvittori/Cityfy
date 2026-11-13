using CityFy.Api.Models;

namespace CityFy.Api.Repositories;

public interface IAnonymousSessionRepository
{
    Task InsertAsync(AnonymousSession session, CancellationToken ct);

    Task<AnonymousSession?> GetAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Moves the expiry forward only if the session is still Active and not expired at <paramref name="now"/>:
    /// false when it expired in the meantime (an expired session is never revived).
    /// </summary>
    Task<bool> RenewAsync(string sessionId, DateTime now, DateTime expiresAt, CancellationToken ct);

    /// <summary>Active sessions past their expiry, and Expired ones whose deletion has to be retried.</summary>
    Task<IReadOnlyList<AnonymousSession>> ListToCleanAsync(DateTime now, int limit, CancellationToken ct);

    /// <summary>Active → Expired (no effect on a session already Expired or Deleted).</summary>
    Task MarkExpiredAsync(string sessionId, CancellationToken ct);

    /// <summary>Any status → Deleted.</summary>
    Task MarkDeletedAsync(string sessionId, DateTime now, CancellationToken ct);

    /// <summary>
    /// Active (not expired) → Transferring to <paramref name="toUserId"/>; true also when it is already
    /// Transferring to the same account (retry). False otherwise: expired, deleted or moved elsewhere.
    /// </summary>
    Task<bool> MarkTransferringAsync(string sessionId, string toUserId, DateTime now, CancellationToken ct);

    /// <summary>Transferring → Transferred.</summary>
    Task MarkTransferredAsync(string sessionId, DateTime now, CancellationToken ct);

    /// <summary>Sessions whose data still has to be moved (oldest first).</summary>
    Task<IReadOnlyList<AnonymousSession>> ListTransferringAsync(int limit, CancellationToken ct);
}
