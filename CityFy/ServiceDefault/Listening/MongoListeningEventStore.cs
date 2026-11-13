using MongoDB.Driver;

namespace ServiceDefault.Listening;

public sealed class MongoListeningEventStore(IMongoDatabase database) : IListeningEventStore
{
    private readonly ListeningEventWriter _writer = new(database.GetCollection<ListeningEvent>(ListeningEvent.CollectionName));

    public Task<int> UpsertAsync(IReadOnlyCollection<ListeningEvent> events, string source, CancellationToken ct = default)
    {
        foreach (var e in events)
            e.Sources = [source];
        return _writer.WriteAsync(events, ct);
    }
}
