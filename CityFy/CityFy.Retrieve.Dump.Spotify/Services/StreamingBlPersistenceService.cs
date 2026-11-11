using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Listening;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public class StreamingBlPersistenceService : IStreamingBlPersistenceService
    {
        private const int MigrationBatchSize = 1000;

        private readonly IStreamingRepository _repository;
        private readonly IListeningEventStore _store;
        private readonly IStreamingMapper _mapper;
        private readonly ILogger<StreamingBlPersistenceService> _logger;

        public StreamingBlPersistenceService(
            IStreamingRepository repository,
            IListeningEventStore store,
            IStreamingMapper mapper,
            ILogger<StreamingBlPersistenceService> logger)
        {
            _repository = repository;
            _store = store;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<int> PersistBufferAsync(string userId, IReadOnlyList<StreamingBl> buffer, string uploadId, CancellationToken ct = default)
        {
            if (buffer.Count == 0) return 0;

            var events = buffer
                .Select(b => _mapper.MapToListeningEvent(b, userId, uploadId))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();

            // Errors propagate: the caller marks the task FAILED instead of reporting a partial import as DONE
            var inserted = await _store.UpsertAsync(events, ListeningSources.Dump, ct);

            if (inserted > 0)
            {
                try
                {
                    await _repository.IncrementTaskInsertedAsync(uploadId, inserted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update task progress for {UploadId}", uploadId);
                }
            }

            return inserted;
        }

        public Task CreateTaskAsync(string uploadId, string userId) =>
            _repository.CreateTaskAsync(new ProcessingTask
            {
                UploadId = uploadId,
                UserId = userId,
                Status = ProcessingStatus.Pending,
                Inserted = 0,
                StartedAt = DateTime.UtcNow
            });

        public async Task ReportProgressAsync(string uploadId, double? percent, string? detail)
        {
            try
            {
                await _repository.UpdateTaskProgressAsync(uploadId, percent, detail);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Progress update failed for {UploadId}", uploadId);
            }
        }

        public Task UpdateTaskStatusAsync(string uploadId, ProcessingStatus status, string? message = null) =>
            _repository.UpdateTaskStatusAsync(uploadId, status, message);

        public Task<IEnumerable<ProcessingTask>> GetTasksByStatusAsync(ProcessingStatus status) =>
            _repository.GetTasksByStatusAsync(status);

        public async Task<int> MigrateLegacyUploadAsync(string uploadId, string userId, CancellationToken ct = default)
        {
            var total = 0;
            await foreach (var batch in _repository.ReadLegacyUploadAsync(uploadId, MigrationBatchSize, ct))
            {
                var events = batch
                    .Select(b => _mapper.MapToListeningEvent(b, userId, uploadId))
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToList();
                total += await _store.UpsertAsync(events, ListeningSources.Dump, ct);
            }

            _logger.LogInformation("Legacy upload {UploadId} migrated for user {UserId}: {Inserted} new plays", uploadId, userId, total);
            return total;
        }
    }
}
