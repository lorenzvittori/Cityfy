using MongoDB.Driver;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Repositories
{
    public class MongoSpotifyRepository : ISpotifyRepository
    {
        private const string TasksCollection = "spotify_retrieve_tasks";

        private readonly IMongoCollection<ProcessingTask> _tasks;

        public MongoSpotifyRepository(IMongoDatabase db)
        {
            _tasks = db.GetCollection<ProcessingTask>(TasksCollection);
        }

        public Task CreateTaskAsync(string jobId, string userId, CancellationToken ct) =>
            _tasks.InsertOneAsync(new ProcessingTask { UploadId = jobId, UserId = userId, Status = ProcessingStatus.Pending }, cancellationToken: ct);

        public Task UpdateTaskAsync(string jobId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null)
        {
            var update = Builders<ProcessingTask>.Update.Set(t => t.Status, status)
                .Set(t => t.ErrorCode, status == ProcessingStatus.FAILED ? errorCode ?? ErrorCodes.SpotifyRetrieveFailed : null);
            if (inserted.HasValue)
                update = update.Set(t => t.Inserted, inserted.Value);
            if (message != null)
                update = update.Set(t => t.Message, message);
            if (status is ProcessingStatus.DONE or ProcessingStatus.FAILED)
                update = update.Set(t => t.CompletedAt, DateTime.UtcNow);

            // Status updates must land even when the job itself was cancelled.
            return _tasks.UpdateOneAsync(t => t.UploadId == jobId, update, cancellationToken: CancellationToken.None);
        }

        public async Task<ProcessingTask?> GetTaskAsync(string jobId, CancellationToken ct) =>
            await _tasks.Find(t => t.UploadId == jobId).FirstOrDefaultAsync(ct);
    }
}
