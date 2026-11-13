using CityFy.Api.Auth;
using CityFy.Api.Models;
using CityFy.Api.SubServices;

namespace CityFy.Api.Services;

public sealed class AnonymousSessionService(
    IAnonymousSessionStoreSubService sessions,
    IUserDataDeletionSubService deletion,
    ILogger<AnonymousSessionService> logger) : IAnonymousSessionService
{
    public async Task<AnonymousSession> StartAsync(CancellationToken ct)
    {
        var session = await sessions.CreateAsync(DateTime.UtcNow, ct);
        logger.LogInformation("Anonymous session {SessionId} started, expires at {ExpiresAt:o}", session.Id, session.ExpiresAt);
        return session;
    }

    public Task<SessionCheck> CheckAsync(string sessionId, CancellationToken ct) =>
        sessions.CheckAsync(sessionId, DateTime.UtcNow, ct);

    public async Task<SessionCleanupResult> CleanupExpiredAsync(CancellationToken ct)
    {
        int deleted = 0, failed = 0;
        foreach (var session in await sessions.ListToCleanAsync(DateTime.UtcNow, ct))
        {
            // Expired first: from now on the cookie is refused even if the deletion fails
            await sessions.MarkExpiredAsync(session.Id, ct);

            var (_, failedServices) = await deletion.DeleteEverywhereAsync(AuthSetup.AnonymousUserId(session.Id), ct);
            if (failedServices.Count > 0)
            {
                failed++;
                logger.LogWarning("Anonymous session {SessionId}: data deletion incomplete ({Services}), retried at the next round",
                    session.Id, string.Join(", ", failedServices));
                continue;
            }

            await sessions.MarkDeletedAsync(session.Id, DateTime.UtcNow, ct);
            deleted++;
        }

        if (deleted + failed > 0)
            logger.LogInformation("Expired anonymous sessions: {Deleted} deleted, {Failed} to retry", deleted, failed);
        return new SessionCleanupResult(deleted, failed);
    }
}
