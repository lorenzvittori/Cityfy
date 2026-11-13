using MongoDB.Driver;

namespace ServiceDefault.Listening;

public sealed class MongoListeningEventTransfer : IListeningEventTransfer
{
    /// <summary>Plays moved per round trip.</summary>
    private const int BatchSize = 1000;

    private readonly IMongoCollection<ListeningEvent> _events;
    private readonly ListeningEventWriter _writer;

    public MongoListeningEventTransfer(IMongoDatabase database)
    {
        _events = database.GetCollection<ListeningEvent>(ListeningEvent.CollectionName);
        _writer = new ListeningEventWriter(_events);
    }

    public async Task<long> TransferAsync(string fromUserId, string toUserId, CancellationToken ct)
    {
        long moved = 0;
        while (true)
        {
            var batch = await _events.Find(e => e.UserId == fromUserId).Limit(BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
                return moved;

            var oldIds = batch.Select(e => e.Id).ToList();
            // New documents first (deduplicated against the plays the target already has, also from
            // other sources), then the old ones: an interruption leaves at most a copy, never a loss
            foreach (var e in batch)
                e.UserId = toUserId;
            await _writer.WriteAsync(batch, ct);
            await _events.DeleteManyAsync(e => oldIds.Contains(e.Id), ct);
            moved += batch.Count;
        }
    }
}
