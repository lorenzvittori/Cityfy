using System.Security.Cryptography;
using CityFy.Api.Models;
using CityFy.Api.Options;
using CityFy.Api.Repositories;
using Microsoft.Extensions.Options;

namespace CityFy.Api.SubServices;

public sealed class AnonymousSessionStoreSubService(IAnonymousSessionRepository repository, IOptions<AnonymousSessionOptions> options)
    : IAnonymousSessionStoreSubService
{
    /// <summary>32 hex characters = 128 random bits: the id is the only secret of the session.</summary>
    private const int IdLength = 32;

    private readonly AnonymousSessionOptions _options = options.Value;

    public async Task<AnonymousSession> CreateAsync(DateTime now, CancellationToken ct)
    {
        var session = new AnonymousSession
        {
            Id = RandomNumberGenerator.GetHexString(IdLength, lowercase: true),
            CreatedAt = now,
            RenewedAt = now,
            ExpiresAt = now + _options.Lifetime,
            Status = AnonymousSessionStatus.Active
        };
        await repository.InsertAsync(session, ct);
        return session;
    }

    public async Task<SessionCheck> CheckAsync(string sessionId, DateTime now, CancellationToken ct)
    {
        var session = await repository.GetAsync(sessionId, ct);
        if (session is not { Status: AnonymousSessionStatus.Active } || session.ExpiresAt <= now)
            return new SessionCheck(SessionCheckOutcome.Invalid, null);

        if (now - session.RenewedAt < _options.RenewAfter)
            return new SessionCheck(SessionCheckOutcome.Valid, session.ExpiresAt);

        var expiresAt = now + _options.Lifetime;
        // false: it expired (or was deleted) between the read and the update
        return await repository.RenewAsync(sessionId, now, expiresAt, ct)
            ? new SessionCheck(SessionCheckOutcome.Renewed, expiresAt)
            : new SessionCheck(SessionCheckOutcome.Invalid, null);
    }

    public Task<IReadOnlyList<AnonymousSession>> ListToCleanAsync(DateTime now, CancellationToken ct) =>
        repository.ListToCleanAsync(now, _options.CleanupBatchSize, ct);

    public Task MarkExpiredAsync(string sessionId, CancellationToken ct) => repository.MarkExpiredAsync(sessionId, ct);

    public Task MarkDeletedAsync(string sessionId, DateTime now, CancellationToken ct) => repository.MarkDeletedAsync(sessionId, now, ct);

    public Task<bool> MarkTransferringAsync(string sessionId, string toUserId, DateTime now, CancellationToken ct) =>
        repository.MarkTransferringAsync(sessionId, toUserId, now, ct);

    public Task MarkTransferredAsync(string sessionId, DateTime now, CancellationToken ct) => repository.MarkTransferredAsync(sessionId, now, ct);

    public Task<IReadOnlyList<AnonymousSession>> ListTransferringAsync(CancellationToken ct) =>
        repository.ListTransferringAsync(_options.CleanupBatchSize, ct);
}
