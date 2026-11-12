using MongoDB.Driver;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Repositories;

public sealed class MongoSpotifyRepository(IMongoDatabase db) : ISpotifyRepository
{
    private const string TasksCollection = "spotify_retrieve_tasks";

    private readonly IMongoCollection<ProcessingTask> _tasks = db.GetCollection<ProcessingTask>(TasksCollection);

    public Task CreateTaskAsync(string jobId, string userId, CancellationToken ct) =>
        _tasks.InsertOneAsync(new ProcessingTask { UploadId = jobId, UserId = userId, Status = ProcessingStatus.Pending }, cancellationToken: ct);

    public Task UpdateTaskAsync(string jobId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null)
    {
        var update = Builders<ProcessingTask>.Update.Set(t => t.Status, status)
            .Set(t => t.ErrorCode, status switch
            {
                ProcessingStatus.Failed => errorCode ?? ErrorCodes.SpotifyRetrieveFailed,
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

        // A cancelled job stays cancelled: a late Done/Failed from the worker must not overwrite it.
        // Status updates must land even when the job itself was cancelled (CancellationToken.None).
        var filter = status == ProcessingStatus.Cancelled
            ? Builders<ProcessingTask>.Filter.Eq(t => t.UploadId, jobId)
            : Builders<ProcessingTask>.Filter.Where(t => t.UploadId == jobId && t.Status != ProcessingStatus.Cancelled);
        return _tasks.UpdateOneAsync(filter, update, cancellationToken: CancellationToken.None);
    }

    public Task UpdateProgressAsync(string jobId, double? percent, string? detail, int inserted) =>
        _tasks.UpdateOneAsync(t => t.UploadId == jobId,
            Builders<ProcessingTask>.Update
                .Set(t => t.ProgressPercent, percent)
                .Set(t => t.ProgressDetail, detail)
                .Set(t => t.Inserted, inserted)
                .Set(t => t.UpdatedAt, DateTime.UtcNow),
            cancellationToken: CancellationToken.None);

    public async Task<ProcessingTask?> GetTaskAsync(string jobId, CancellationToken ct) =>
        await _tasks.Find(t => t.UploadId == jobId).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProcessingTask>> ListTasksAsync(int limit, CancellationToken ct) =>
        await _tasks.Find(Builders<ProcessingTask>.Filter.Empty)
            .SortByDescending(t => t.StartedAt).Limit(limit).ToListAsync(ct);

    public async Task<IReadOnlyList<ProcessingTask>> ListActiveTasksAsync(string userId, CancellationToken ct) =>
        await _tasks.Find(t => t.UserId == userId &&
                               (t.Status == ProcessingStatus.Pending || t.Status == ProcessingStatus.InProgress))
            .ToListAsync(ct);

    public async Task<long> DeleteTasksAsync(string userId, CancellationToken ct) =>
        (await _tasks.DeleteManyAsync(t => t.UserId == userId, ct)).DeletedCount;
}
