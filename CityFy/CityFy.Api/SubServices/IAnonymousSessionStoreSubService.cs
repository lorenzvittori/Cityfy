using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

/// <summary>Anonymous sessions: creation, validity and renewal rules, cleanup bookkeeping.</summary>
public interface IAnonymousSessionStoreSubService
{
    /// <summary>New Active session expiring at now + Lifetime.</summary>
    Task<AnonymousSession> CreateAsync(DateTime now, CancellationToken ct);

    /// <summary>
    /// Invalid when the session is unknown, not Active or past its expiry. Otherwise Valid, or Renewed
    /// (expiry moved to now + Lifetime) when the last renewal is older than RenewAfter.
    /// </summary>
    Task<SessionCheck> CheckAsync(string sessionId, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<AnonymousSession>> ListToCleanAsync(DateTime now, CancellationToken ct);

    Task MarkExpiredAsync(string sessionId, CancellationToken ct);

    Task MarkDeletedAsync(string sessionId, DateTime now, CancellationToken ct);

    /// <summary>See <see cref="Repositories.IAnonymousSessionRepository.MarkTransferringAsync"/>.</summary>
    Task<bool> MarkTransferringAsync(string sessionId, string toUserId, DateTime now, CancellationToken ct);

    Task MarkTransferredAsync(string sessionId, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<AnonymousSession>> ListTransferringAsync(CancellationToken ct);
}
