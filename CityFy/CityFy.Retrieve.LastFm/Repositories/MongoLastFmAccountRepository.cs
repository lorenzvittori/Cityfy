using CityFy.Retrieve.LastFm.Models;
using MongoDB.Driver;

namespace CityFy.Retrieve.LastFm.Repositories;

public sealed class MongoLastFmAccountRepository : ILastFmAccountRepository
{
    private readonly IMongoCollection<LastFmAccount> _accounts;
    private readonly Lazy<Task> _indexes;

    public MongoLastFmAccountRepository(IMongoDatabase db)
    {
        _accounts = db.GetCollection<LastFmAccount>(LastFmAccount.CollectionName);
        var keys = Builders<LastFmAccount>.IndexKeys;
        _indexes = new Lazy<Task>(() => _accounts.Indexes.CreateManyAsync(
        [
            // one CityFy user per Last.fm account
            new CreateIndexModel<LastFmAccount>(keys.Ascending(a => a.Username), new CreateIndexOptions { Unique = true }),
            // the polling query
            new CreateIndexModel<LastFmAccount>(keys.Ascending(a => a.Status).Ascending(a => a.NextPollAt))
        ]));
    }

    public async Task<LastFmAccount?> GetAsync(string userId, CancellationToken ct)
    {
        await _indexes.Value;
        return await _accounts.Find(a => a.UserId == userId).FirstOrDefaultAsync(ct);
    }

    public async Task<LastFmAccount?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        await _indexes.Value;
        return await _accounts.Find(a => a.Username == username).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<LastFmAccount>> ListAsync(CancellationToken ct)
    {
        await _indexes.Value;
        return await _accounts.Find(Builders<LastFmAccount>.Filter.Empty).SortBy(a => a.Username).ToListAsync(ct);
    }

    public async Task UpsertAsync(LastFmAccount account, CancellationToken ct)
    {
        await _indexes.Value;
        await _accounts.ReplaceOneAsync(a => a.UserId == account.UserId, account, new ReplaceOptions { IsUpsert = true }, ct);
    }

    public async Task<IReadOnlyList<LastFmAccount>> ListDueAsync(DateTime now, int limit, CancellationToken ct)
    {
        await _indexes.Value;
        return await _accounts.Find(a => a.Status == LastFmAccountStatus.Active && a.NextPollAt <= now)
            .SortBy(a => a.NextPollAt).Limit(limit).ToListAsync(ct);
    }

    public async Task UpdatePollAsync(string userId, DateTime polledAt, DateTime lastScrobbleAt, DateTime? lastListeningAt, DateTime nextPollAt, CancellationToken ct)
    {
        await _indexes.Value;
        var u = Builders<LastFmAccount>.Update;
        var update = u.Set(a => a.LastPolledAt, polledAt).Set(a => a.NextPollAt, nextPollAt).Max(a => a.LastScrobbleAt, lastScrobbleAt);
        if (lastListeningAt is { } listening)
            update = update.Max(a => a.LastListeningAt, listening);
        await _accounts.UpdateOneAsync(a => a.UserId == userId, update, cancellationToken: ct);
    }

    public async Task SuspendAsync(string userId, string errorCode, CancellationToken ct)
    {
        await _indexes.Value;
        await _accounts.UpdateOneAsync(a => a.UserId == userId,
            Builders<LastFmAccount>.Update.Set(a => a.Status, LastFmAccountStatus.Suspended).Set(a => a.ErrorCode, errorCode),
            cancellationToken: ct);
    }

    public async Task SetNextPollAsync(string userId, DateTime nextPollAt, CancellationToken ct)
    {
        await _indexes.Value;
        await _accounts.UpdateOneAsync(a => a.UserId == userId,
            Builders<LastFmAccount>.Update.Set(a => a.NextPollAt, nextPollAt), cancellationToken: ct);
    }

    public async Task PullNextPollAsync(string userId, DateTime at, CancellationToken ct)
    {
        await _indexes.Value;
        await _accounts.UpdateOneAsync(a => a.UserId == userId,
            Builders<LastFmAccount>.Update.Min(a => a.NextPollAt, at), cancellationToken: ct);
    }

    public async Task SetHistoryImportAsync(string userId, LastFmHistoryImport? import, CancellationToken ct)
    {
        await _indexes.Value;
        await _accounts.UpdateOneAsync(a => a.UserId == userId,
            Builders<LastFmAccount>.Update.Set(a => a.HistoryImport, import), cancellationToken: ct);
    }

    public async Task<long> DeleteAsync(string userId, CancellationToken ct)
    {
        await _indexes.Value;
        return (await _accounts.DeleteOneAsync(a => a.UserId == userId, ct)).DeletedCount;
    }

    public async Task<long> DeleteAllAsync(CancellationToken ct) =>
        (await _accounts.DeleteManyAsync(Builders<LastFmAccount>.Filter.Empty, ct)).DeletedCount;
}
