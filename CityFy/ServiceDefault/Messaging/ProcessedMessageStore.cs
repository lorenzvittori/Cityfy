using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace ServiceDefault.Messaging
{
    /// <summary>
    /// Consumer-side deduplication: RabbitMQ delivers at least once (e.g. redelivery after a
    /// crash before the ack), so a consumer records the message ids it already handled.
    /// </summary>
    public interface IProcessedMessageStore
    {
        Task<bool> IsProcessedAsync(string consumer, string messageId, CancellationToken ct);
        Task MarkProcessedAsync(string consumer, string messageId, CancellationToken ct);
    }

    /// <summary>'processed_messages' collection, entries expire after <see cref="Retention"/> (TTL index).</summary>
    public sealed class MongoProcessedMessageStore : IProcessedMessageStore
    {
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        private sealed class Entry
        {
            [BsonId] public string Id { get; set; } = null!;
            [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime ProcessedAt { get; set; }
        }

        private readonly IMongoCollection<Entry> _collection;
        private readonly Lazy<Task> _indexes;

        public MongoProcessedMessageStore(IMongoDatabase db)
        {
            _collection = db.GetCollection<Entry>("processed_messages");
            _indexes = new Lazy<Task>(() => _collection.Indexes.CreateOneAsync(new CreateIndexModel<Entry>(
                Builders<Entry>.IndexKeys.Ascending(e => e.ProcessedAt),
                new CreateIndexOptions { ExpireAfter = Retention })));
        }

        public async Task<bool> IsProcessedAsync(string consumer, string messageId, CancellationToken ct)
        {
            await _indexes.Value;
            var id = Key(consumer, messageId);
            return await _collection.Find(e => e.Id == id).AnyAsync(ct);
        }

        public async Task MarkProcessedAsync(string consumer, string messageId, CancellationToken ct)
        {
            await _indexes.Value;
            var id = Key(consumer, messageId);
            await _collection.ReplaceOneAsync(e => e.Id == id, new Entry { Id = id, ProcessedAt = DateTime.UtcNow },
                new ReplaceOptions { IsUpsert = true }, ct);
        }

        private static string Key(string consumer, string messageId) => $"{consumer}:{messageId}";
    }
}
