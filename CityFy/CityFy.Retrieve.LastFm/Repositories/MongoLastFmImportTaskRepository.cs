using MongoDB.Driver;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Repositories;

public sealed class MongoLastFmImportTaskRepository(IMongoDatabase db) : ILastFmImportTaskRepository
{
    public const string CollectionName = "lastfm_import_tasks";

    private readonly IMongoCollection<ProcessingTask> _tasks = db.GetCollection<ProcessingTask>(CollectionName);

    public Task CreateAsync(string importId, string userId, CancellationToken ct) =>
        _tasks.InsertOneAsync(new ProcessingTask { UploadId = importId, UserId = userId, Status = ProcessingStatus.Pending }, cancellationToken: ct);

    public async Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct) =>
        await _tasks.Find(t => t.UploadId == importId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct) =>
        await _tasks.Find(Builders<ProcessingTask>.Filter.Empty).SortByDescending(t => t.StartedAt).Limit(limit).ToListAsync(ct);

    public async Task<IReadOnlyList<ProcessingTask>> ListUnfinishedAsync(string? userId, CancellationToken ct)
    {
        var f = Builders<ProcessingTask>.Filter;
        var filter = f.In(t => t.Status, [ProcessingStatus.Pending, ProcessingStatus.InProgress]);
        if (userId is not null)
            filter &= f.Eq(t => t.UserId, userId);
        return await _tasks.Find(filter).SortBy(t => t.StartedAt).ToListAsync(ct);
    }

    public Task UpdateStatusAsync(string importId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null)
    {
        var update = Builders<ProcessingTask>.Update.Set(t => t.Status, status)
            .Set(t => t.ErrorCode, status switch
            {
                ProcessingStatus.Failed => errorCode ?? ErrorCodes.LastFmImportFailed,
                ProcessingStatus.Cancelled => ErrorCodes.ProcessCancelled,
                _ => null
            })
            .Set(t => t.UpdatedAt, DateTime.UtcNow);
        if (inserted.HasValue)
            update = update.Set(t => t.Inserted, inserted.Value);
        if (message != null)
            update = update.Set(t => t.Message, message);
        if (status is ProcessingStatus.Done or ProcessingStatus.Failed or ProcessingStatus.Cancelled)
            update = update.Set(t => t.CompletedAt, DateTime.UtcNow).Set(t => t.ProgressDetail, null);
        if (status == ProcessingStatus.Done)
            update = update.Set(t => t.ProgressPercent, 100);

        var filter = status == ProcessingStatus.Cancelled
            ? Builders<ProcessingTask>.Filter.Eq(t => t.UploadId, importId)
            : Builders<ProcessingTask>.Filter.Where(t => t.UploadId == importId && t.Status != ProcessingStatus.Cancelled);
        return _tasks.UpdateOneAsync(filter, update, cancellationToken: CancellationToken.None);
    }

    public Task UpdateProgressAsync(string importId, double? percent, string? detail, int inserted) =>
        _tasks.UpdateOneAsync(t => t.UploadId == importId && t.Status == ProcessingStatus.InProgress,
            Builders<ProcessingTask>.Update
                .Set(t => t.ProgressPercent, percent)
                .Set(t => t.ProgressDetail, detail)
                .Set(t => t.Inserted, inserted)
                .Set(t => t.UpdatedAt, DateTime.UtcNow),
            cancellationToken: CancellationToken.None);

    public async Task<long> DeleteAllAsync(string userId, CancellationToken ct) =>
        (await _tasks.DeleteManyAsync(t => t.UserId == userId, ct)).DeletedCount;

    public async Task<long> DeleteEverythingAsync(CancellationToken ct) =>
        (await _tasks.DeleteManyAsync(Builders<ProcessingTask>.Filter.Empty, ct)).DeletedCount;
}
