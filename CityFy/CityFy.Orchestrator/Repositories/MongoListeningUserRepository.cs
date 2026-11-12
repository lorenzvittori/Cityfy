using MongoDB.Driver;
using ServiceDefault.Listening;

namespace CityFy.Orchestrator.Repositories;

public sealed class MongoListeningUserRepository(IMongoDatabase db) : IListeningUserRepository
{
    private readonly IMongoCollection<ListeningEvent> _events = db.GetCollection<ListeningEvent>(ListeningEvent.CollectionName);

    public async Task<IReadOnlyList<string>> GetUserIdsWithPlaysAsync(CancellationToken ct)
    {
        using var cursor = await _events.DistinctAsync<string>(nameof(ListeningEvent.UserId),
            Builders<ListeningEvent>.Filter.Empty, cancellationToken: ct);
        return await cursor.ToListAsync(ct);
    }
}
