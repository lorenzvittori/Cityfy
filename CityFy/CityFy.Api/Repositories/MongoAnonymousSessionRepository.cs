using CityFy.Api.Models;
using MongoDB.Driver;

namespace CityFy.Api.Repositories;

public sealed class MongoAnonymousSessionRepository : IAnonymousSessionRepository
{
    /// <summary>Deleted sessions stay as a trace (ids and dates only) for this long.</summary>
    public static readonly TimeSpan DeletedRetention = TimeSpan.FromDays(7);

    private readonly IMongoCollection<AnonymousSession> _sessions;
    private readonly Lazy<Task> _indexes;

    public MongoAnonymousSessionRepository(IMongoDatabase db)
    {
        _sessions = db.GetCollection<AnonymousSession>(AnonymousSession.CollectionName);
        _indexes = new Lazy<Task>(() => _sessions.Indexes.CreateManyAsync(
        [
            // the cleanup query
            new CreateIndexModel<AnonymousSession>(Builders<AnonymousSession>.IndexKeys
                .Ascending(s => s.Status).Ascending(s => s.ExpiresAt)),
            // only documents with DeletedAt set expire
            new CreateIndexModel<AnonymousSession>(Builders<AnonymousSession>.IndexKeys.Ascending(s => s.DeletedAt),
                new CreateIndexOptions { ExpireAfter = DeletedRetention })
        ]));
    }

    public async Task InsertAsync(AnonymousSession session, CancellationToken ct)
    {
        await _indexes.Value;
        await _sessions.InsertOneAsync(session, cancellationToken: ct);
    }

    public async Task<AnonymousSession?> GetAsync(string sessionId, CancellationToken ct)
    {
        await _indexes.Value;
        return await _sessions.Find(s => s.Id == sessionId).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> RenewAsync(string sessionId, DateTime now, DateTime expiresAt, CancellationToken ct)
    {
        await _indexes.Value;
        var result = await _sessions.UpdateOneAsync(
            s => s.Id == sessionId && s.Status == AnonymousSessionStatus.Active && s.ExpiresAt > now,
            Builders<AnonymousSession>.Update.Set(s => s.RenewedAt, now).Set(s => s.ExpiresAt, expiresAt),
            cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<IReadOnlyList<AnonymousSession>> ListToCleanAsync(DateTime now, int limit, CancellationToken ct)
    {
        await _indexes.Value;
        return await _sessions
            .Find(s => (s.Status == AnonymousSessionStatus.Active && s.ExpiresAt <= now) || s.Status == AnonymousSessionStatus.Expired)
            .SortBy(s => s.ExpiresAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task MarkExpiredAsync(string sessionId, CancellationToken ct)
    {
        await _indexes.Value;
        await _sessions.UpdateOneAsync(s => s.Id == sessionId && s.Status == AnonymousSessionStatus.Active,
            Builders<AnonymousSession>.Update.Set(s => s.Status, AnonymousSessionStatus.Expired), cancellationToken: ct);
    }

    public async Task<bool> MarkTransferringAsync(string sessionId, string toUserId, DateTime now, CancellationToken ct)
    {
        await _indexes.Value;
        var result = await _sessions.UpdateOneAsync(
            s => s.Id == sessionId &&
                 ((s.Status == AnonymousSessionStatus.Active && s.ExpiresAt > now) ||
                  (s.Status == AnonymousSessionStatus.Transferring && s.TransferTo == toUserId)),
            Builders<AnonymousSession>.Update.Set(s => s.Status, AnonymousSessionStatus.Transferring).Set(s => s.TransferTo, toUserId),
            cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task MarkTransferredAsync(string sessionId, DateTime now, CancellationToken ct)
    {
        await _indexes.Value;
        await _sessions.UpdateOneAsync(s => s.Id == sessionId && s.Status == AnonymousSessionStatus.Transferring,
            Builders<AnonymousSession>.Update.Set(s => s.Status, AnonymousSessionStatus.Transferred).Set(s => s.DeletedAt, now),
            cancellationToken: ct);
    }

    public async Task<IReadOnlyList<AnonymousSession>> ListTransferringAsync(int limit, CancellationToken ct)
    {
        await _indexes.Value;
        return await _sessions.Find(s => s.Status == AnonymousSessionStatus.Transferring)
            .SortBy(s => s.ExpiresAt).Limit(limit).ToListAsync(ct);
    }

    public async Task MarkDeletedAsync(string sessionId, DateTime now, CancellationToken ct)
    {
        await _indexes.Value;
        await _sessions.UpdateOneAsync(s => s.Id == sessionId,
            Builders<AnonymousSession>.Update.Set(s => s.Status, AnonymousSessionStatus.Deleted).Set(s => s.DeletedAt, now),
            cancellationToken: ct);
    }
}
