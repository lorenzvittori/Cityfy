using CityFy.Api.Models;

namespace CityFy.Api.Services;

/// <summary>Anonymous sessions: a visitor uses CityFy without an account for <c>Sessions:Lifetime</c> after the last visit.</summary>
public interface IAnonymousSessionService
{
    /// <summary>Creates a new session (the caller signs the cookie in).</summary>
    Task<AnonymousSession> StartAsync(CancellationToken ct);

    /// <summary>Validity of the session of a request; renews it when due (see <see cref="SessionCheckOutcome"/>).</summary>
    Task<SessionCheck> CheckAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Deletes on every service the data of the sessions past their expiry. A session whose deletion
    /// did not complete stays Expired and is retried at the next round.
    /// </summary>
    Task<SessionCleanupResult> CleanupExpiredAsync(CancellationToken ct);
}
