using System.Globalization;
using System.Runtime.CompilerServices;
using CityFy.Retrieve.Dump.Spotify.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Repositories
{
    public class MongoStreamingRepository : IStreamingRepository
    {
        private readonly IMongoDatabase _database;
        private readonly MongoOptions _options;

        public MongoStreamingRepository(IMongoDatabase database, MongoOptions options)
        {
            _database = database;
            _options = options;
        }

        private IMongoCollection<ProcessingTask> Tasks => _database.GetCollection<ProcessingTask>(_options.TaskCollectionName);

        public async Task CreateTaskAsync(ProcessingTask task)
        {
            var filter = Builders<ProcessingTask>.Filter.Eq(t => t.UploadId, task.UploadId);
            await Tasks.ReplaceOneAsync(filter, task, new ReplaceOptions { IsUpsert = true });
        }

        public async Task IncrementTaskInsertedAsync(string uploadId, int delta)
        {
            var update = Builders<ProcessingTask>.Update.Inc(t => t.Inserted, delta);
            await Tasks.UpdateOneAsync(t => t.UploadId == uploadId, update);
        }

        public async Task UpdateTaskStatusAsync(string uploadId, ProcessingStatus status, string? message = null, string? errorCode = null)
        {
            var updateDef = Builders<ProcessingTask>.Update.Set(t => t.Status, status);
            if (!string.IsNullOrWhiteSpace(message))
                updateDef = updateDef.Set(t => t.Message, message);
            // The code is cleared when the task leaves FAILED (e.g. re-processed)
            updateDef = updateDef.Set(t => t.ErrorCode, status switch
                {
                    ProcessingStatus.FAILED => errorCode ?? ServiceDefault.Errors.ErrorCodes.UploadProcessingFailed,
                    ProcessingStatus.CANCELLED => ServiceDefault.Errors.ErrorCodes.ProcessCancelled,
                    _ => null
                })
                .Set(t => t.UpdatedAt, DateTime.UtcNow);
            if (status is ProcessingStatus.DONE or ProcessingStatus.FAILED or ProcessingStatus.CANCELLED)
                updateDef = updateDef.Set(t => t.CompletedAt, DateTime.UtcNow);
            if (status == ProcessingStatus.DONE)
                updateDef = updateDef.Set(t => t.ProgressPercent, 100).Set(t => t.ProgressDetail, null);

            // A cancelled task stays cancelled: a late DONE/FAILED from the import must not overwrite it
            var filter = status == ProcessingStatus.CANCELLED
                ? Builders<ProcessingTask>.Filter.Eq(t => t.UploadId, uploadId)
                : Builders<ProcessingTask>.Filter.Where(t => t.UploadId == uploadId && t.Status != ProcessingStatus.CANCELLED);
            await Tasks.UpdateOneAsync(filter, updateDef);
        }

        public Task UpdateTaskProgressAsync(string uploadId, double? percent, string? detail) =>
            Tasks.UpdateOneAsync(t => t.UploadId == uploadId,
                Builders<ProcessingTask>.Update
                    .Set(t => t.ProgressPercent, percent.HasValue ? Math.Round(percent.Value, 1) : null)
                    .Set(t => t.ProgressDetail, detail)
                    .Set(t => t.UpdatedAt, DateTime.UtcNow));

        public async Task<IEnumerable<ProcessingTask>> GetTasksByStatusAsync(ProcessingStatus status) =>
            await Tasks.Find(t => t.Status == status).ToListAsync();

        public async Task<ProcessingTask?> GetTaskByUploadIdAsync(string uploadId) =>
            await Tasks.Find(t => t.UploadId == uploadId).FirstOrDefaultAsync();

        public async Task<IEnumerable<ProcessingTask>> GetTasksPagedAsync(int page, int pageSize)
        {
            var skip = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
            return await Tasks.Find(Builders<ProcessingTask>.Filter.Empty)
                .SortByDescending(t => t.StartedAt) // most recent first (admin panel)
                .Skip(skip).Limit(Math.Max(1, pageSize)).ToListAsync();
        }

        public async Task<IEnumerable<string>> GetDocumentsPagedAsync(string collectionName, int page, int pageSize, string? providedUploadId = null)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);
            var skip = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
            FilterDefinition<BsonDocument> filter = FilterDefinition<BsonDocument>.Empty;
            if (!string.IsNullOrWhiteSpace(providedUploadId))
                filter = Builders<BsonDocument>.Filter.Eq("_uploadId", providedUploadId);

            var docs = await collection.Find(filter).Skip(skip).Limit(Math.Max(1, pageSize)).ToListAsync();
            return docs.Select(d => d.ToJson());
        }

        public async IAsyncEnumerable<IReadOnlyList<StreamingBl>> ReadLegacyUploadAsync(
            string uploadId, int batchSize, [EnumeratorCancellation] CancellationToken ct)
        {
            var collection = _database.GetCollection<BsonDocument>(_options.CollectionPrefix + uploadId);
            using var cursor = await collection.FindAsync(
                FilterDefinition<BsonDocument>.Empty,
                new FindOptions<BsonDocument> { BatchSize = batchSize },
                ct);

            while (await cursor.MoveNextAsync(ct))
            {
                var batch = cursor.Current.Select(FromLegacy).Where(b => b != null).Select(b => b!).ToList();
                if (batch.Count > 0)
                    yield return batch;
            }
        }

        // Legacy documents were written as System.Text.Json(StreamingBl) -> BsonDocument.Parse,
        // so field names are PascalCase and dates are ISO strings.
        private static StreamingBl? FromLegacy(BsonDocument d)
        {
            var ts = d.GetValue("Timestamp", BsonNull.Value);
            DateTime timestamp;
            if (ts.IsBsonDateTime)
                timestamp = ts.ToUniversalTime();
            else if (ts.IsString && DateTime.TryParse(ts.AsString, CultureInfo.InvariantCulture,
                         DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                timestamp = parsed;
            else
                return null;

            return new StreamingBl
            {
                Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
                MsPlayed = d.GetValue("MsPlayed", 0).ToInt32(),
                TrackName = Str(d, "TrackName"),
                ArtistName = Str(d, "ArtistName"),
                AlbumName = Str(d, "AlbumName"),
                SpotifyTrackUri = Str(d, "SpotifyTrackUri"),
                Skipped = d.GetValue("Skipped", false).ToBoolean(),
            };
        }

        private static string? Str(BsonDocument d, string name) =>
            d.TryGetValue(name, out var v) && v.IsString ? v.AsString : null;
    }
}
