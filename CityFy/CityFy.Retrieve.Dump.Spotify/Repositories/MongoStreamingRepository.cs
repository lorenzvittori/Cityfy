using System.Globalization;
using System.Runtime.CompilerServices;
using CityFy.Retrieve.Dump.Spotify.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Repositories;

public sealed class MongoStreamingRepository(IMongoDatabase database, MongoOptions options) : IStreamingRepository
{
    private IMongoCollection<ProcessingTask> Tasks => database.GetCollection<ProcessingTask>(options.TaskCollectionName);

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
        // The code is cleared when the task leaves Failed (e.g. re-processed)
        updateDef = updateDef.Set(t => t.ErrorCode, status switch
            {
                ProcessingStatus.Failed => errorCode ?? ServiceDefault.Errors.ErrorCodes.UploadProcessingFailed,
                ProcessingStatus.Cancelled => ServiceDefault.Errors.ErrorCodes.ProcessCancelled,
                _ => null
            })
            .Set(t => t.UpdatedAt, DateTime.UtcNow);
        if (status is ProcessingStatus.Done or ProcessingStatus.Failed or ProcessingStatus.Cancelled)
            updateDef = updateDef.Set(t => t.CompletedAt, DateTime.UtcNow);
        if (status == ProcessingStatus.Done)
            updateDef = updateDef.Set(t => t.ProgressPercent, 100).Set(t => t.ProgressDetail, null);

        // A cancelled task stays cancelled: a late Done/Failed from the import must not overwrite it
        var filter = status == ProcessingStatus.Cancelled
            ? Builders<ProcessingTask>.Filter.Eq(t => t.UploadId, uploadId)
            : Builders<ProcessingTask>.Filter.Where(t => t.UploadId == uploadId && t.Status != ProcessingStatus.Cancelled);
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
        var collection = database.GetCollection<BsonDocument>(collectionName);
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
        var collection = database.GetCollection<BsonDocument>(options.CollectionPrefix + uploadId);
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

    public async Task<IReadOnlyList<ProcessingTask>> GetTasksByUserAsync(string userId, CancellationToken ct) =>
        await Tasks.Find(t => t.UserId == userId).ToListAsync(ct);

    public async Task<long> DeleteTasksByUserAsync(string userId, CancellationToken ct) =>
        (await Tasks.DeleteManyAsync(t => t.UserId == userId, ct)).DeletedCount;

    public async Task DropLegacyUploadAsync(string uploadId, CancellationToken ct)
    {
        var name = options.CollectionPrefix + uploadId;
        var existing = await (await database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = new BsonDocument("name", name) }, ct)).ToListAsync(ct);
        if (existing.Count > 0)
            await database.DropCollectionAsync(name, ct);
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
