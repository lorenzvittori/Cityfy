using CityFy.Api.Models;
using MongoDB.Driver;

namespace CityFy.Api.Repositories;

public sealed class MongoUserRepository : IUserRepository
{
    private readonly IMongoCollection<CityFyUser> _users;
    private readonly Lazy<Task> _indexes;

    public MongoUserRepository(IMongoDatabase db)
    {
        _users = db.GetCollection<CityFyUser>(CityFyUser.CollectionName);
        _indexes = new Lazy<Task>(() => _users.Indexes.CreateOneAsync(new CreateIndexModel<CityFyUser>(
            Builders<CityFyUser>.IndexKeys.Ascending(u => u.LastFmUsername), new CreateIndexOptions { Unique = true })));
    }

    public async Task<CityFyUser?> GetAsync(string userId, CancellationToken ct)
    {
        await _indexes.Value;
        return await _users.Find(u => u.Id == userId).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> RecordLoginAsync(string userId, string lastFmUsername, DateTime now, CancellationToken ct)
    {
        await _indexes.Value;
        var result = await _users.UpdateOneAsync(u => u.Id == userId,
            Builders<CityFyUser>.Update
                .SetOnInsert(u => u.LastFmUsername, lastFmUsername)
                .SetOnInsert(u => u.CreatedAt, now)
                .Set(u => u.LastLoginAt, now),
            new UpdateOptions { IsUpsert = true }, ct);
        return result.UpsertedId is not null;
    }

    public async Task<long> DeleteAsync(string userId, CancellationToken ct)
    {
        await _indexes.Value;
        return (await _users.DeleteOneAsync(u => u.Id == userId, ct)).DeletedCount;
    }

    public async Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct)
    {
        await _indexes.Value;
        return await _users.Find(Builders<CityFyUser>.Filter.Empty).Project(u => u.Id).ToListAsync(ct);
    }
}
