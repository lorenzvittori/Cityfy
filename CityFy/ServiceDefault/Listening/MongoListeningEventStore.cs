using MongoDB.Driver;

namespace ServiceDefault.Listening;

public sealed class MongoListeningEventStore(IMongoDatabase database) : IListeningEventStore
{
    private readonly IMongoCollection<ListeningEvent> _collection = database.GetCollection<ListeningEvent>(ListeningEvent.CollectionName);
    // Created once, on the first write
    private readonly Lazy<Task> _indexes = new(() => EnsureIndexesAsync(database.GetCollection<ListeningEvent>(ListeningEvent.CollectionName)));

    public async Task<int> UpsertAsync(IReadOnlyCollection<ListeningEvent> events, string source, CancellationToken ct = default)
    {
        if (events.Count == 0)
            return 0;

        await _indexes.Value;

        var now = DateTime.UtcNow;
        var u = Builders<ListeningEvent>.Update;

        var writes = events
            .Where(e => !string.IsNullOrEmpty(e.UserId) && !string.IsNullOrEmpty(e.TrackUri))
            // Same play twice in one batch: keep one, otherwise the unordered bulk counts it twice
            .GroupBy(e => ListeningEvent.BuildId(e.UserId, e.TrackUri, e.PlayedAt))
            .Select(g =>
            {
                var e = g.First();
                var updates = new List<UpdateDefinition<ListeningEvent>>
                {
                    u.SetOnInsert(x => x.UserId, e.UserId),
                    u.SetOnInsert(x => x.PlayedAt, e.PlayedAt.ToUniversalTime()),
                    u.SetOnInsert(x => x.TrackUri, e.TrackUri),
                    u.SetOnInsert(x => x.SourceRef, e.SourceRef),
                    u.SetOnInsert(x => x.ImportedAt, now),
                    u.AddToSet(x => x.Sources, source),
                };

                // Fields only some sources know: set them when present (the richer source
                // wins), otherwise only initialise them on insert.
                AddField(updates, x => x.MsPlayed, e.MsPlayed);
                AddField(updates, x => x.Skipped, e.Skipped);
                AddField(updates, x => x.TrackName, e.TrackName);
                AddField(updates, x => x.ArtistName, e.ArtistName);
                AddField(updates, x => x.ArtistUri, e.ArtistUri);
                AddField(updates, x => x.AlbumName, e.AlbumName);
                AddField(updates, x => x.ContextUri, e.ContextUri);

                return new UpdateOneModel<ListeningEvent>(
                    Builders<ListeningEvent>.Filter.Eq(x => x.Id, g.Key), u.Combine(updates))
                { IsUpsert = true };
            })
            .ToList();

        if (writes.Count == 0)
            return 0;

        var result = await _collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
        return result.Upserts.Count;
    }

    private static void AddField<T>(
        List<UpdateDefinition<ListeningEvent>> updates,
        System.Linq.Expressions.Expression<Func<ListeningEvent, T>> field,
        T value)
    {
        var u = Builders<ListeningEvent>.Update;
        updates.Add(value is null ? u.SetOnInsert(field, value) : u.Set(field, value));
    }

    private static Task EnsureIndexesAsync(IMongoCollection<ListeningEvent> collection)
    {
        var keys = Builders<ListeningEvent>.IndexKeys;
        return collection.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<ListeningEvent>(keys.Ascending(x => x.UserId).Descending(x => x.PlayedAt)),
            new CreateIndexModel<ListeningEvent>(keys.Ascending(x => x.UserId).Ascending(x => x.ArtistName)),
        });
    }
}
