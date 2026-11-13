using System.Collections.Concurrent;
using CityFy.Api.Models;
using CityFy.Api.Repositories;

namespace CityFy.Api.Tests;

/// <summary>'anonymous_sessions' in memory, with the same filters as the Mongo repository.</summary>
public sealed class InMemoryAnonymousSessionRepository : IAnonymousSessionRepository
{
    public ConcurrentDictionary<string, AnonymousSession> Sessions { get; } = new();

    public Task InsertAsync(AnonymousSession session, CancellationToken ct)
    {
        Sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<AnonymousSession?> GetAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(Sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<bool> RenewAsync(string sessionId, DateTime now, DateTime expiresAt, CancellationToken ct)
    {
        if (!Sessions.TryGetValue(sessionId, out var s) || s.Status != AnonymousSessionStatus.Active || s.ExpiresAt <= now)
            return Task.FromResult(false);
        s.RenewedAt = now;
        s.ExpiresAt = expiresAt;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<AnonymousSession>> ListToCleanAsync(DateTime now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AnonymousSession>>(Sessions.Values
            .Where(s => (s.Status == AnonymousSessionStatus.Active && s.ExpiresAt <= now) || s.Status == AnonymousSessionStatus.Expired)
            .OrderBy(s => s.ExpiresAt)
            .Take(limit)
            .ToList());

    public Task MarkExpiredAsync(string sessionId, CancellationToken ct)
    {
        if (Sessions.TryGetValue(sessionId, out var s) && s.Status == AnonymousSessionStatus.Active)
            s.Status = AnonymousSessionStatus.Expired;
        return Task.CompletedTask;
    }

    public Task<bool> MarkTransferringAsync(string sessionId, string toUserId, DateTime now, CancellationToken ct)
    {
        if (!Sessions.TryGetValue(sessionId, out var s) ||
            !((s.Status == AnonymousSessionStatus.Active && s.ExpiresAt > now) ||
              (s.Status == AnonymousSessionStatus.Transferring && s.TransferTo == toUserId)))
            return Task.FromResult(false);
        s.Status = AnonymousSessionStatus.Transferring;
        s.TransferTo = toUserId;
        return Task.FromResult(true);
    }

    public Task MarkTransferredAsync(string sessionId, DateTime now, CancellationToken ct)
    {
        if (Sessions.TryGetValue(sessionId, out var s) && s.Status == AnonymousSessionStatus.Transferring)
        {
            s.Status = AnonymousSessionStatus.Transferred;
            s.DeletedAt = now;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnonymousSession>> ListTransferringAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AnonymousSession>>(Sessions.Values
            .Where(s => s.Status == AnonymousSessionStatus.Transferring).OrderBy(s => s.ExpiresAt).Take(limit).ToList());

    public Task MarkDeletedAsync(string sessionId, DateTime now, CancellationToken ct)
    {
        if (Sessions.TryGetValue(sessionId, out var s))
        {
            s.Status = AnonymousSessionStatus.Deleted;
            s.DeletedAt = now;
        }
        return Task.CompletedTask;
    }
}
