using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Models;
using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public class StreamingBlPersistenceService : IStreamingBlPersistenceService
    {
        private readonly IStreamingRepository _repository;
        private readonly Microsoft.Extensions.Logging.ILogger<StreamingBlPersistenceService> _logger;

        public StreamingBlPersistenceService(IStreamingRepository repository, Microsoft.Extensions.Logging.ILogger<StreamingBlPersistenceService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task EnsureCollectionExistsAsync(string collectionName)
        {
            await _repository.EnsureCollectionExistsAsync(collectionName);
        }

        public async Task<int> PersistBufferAsync(string collectionName, IEnumerable<StreamingBl> buffer, string uploadId)
        {
            if (buffer == null) return 0;
            var list = buffer as IList<StreamingBl> ?? new List<StreamingBl>(buffer);
            if (list.Count == 0) return 0;

            try
            {
                await _repository.InsertManyAsync(collectionName, list, uploadId);
                // update task progress
                try
                {
                    await _repository.IncrementTaskInsertedAsync(uploadId, list.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update task progress: {Message}", ex.Message);
                }
                return list.Count;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to persist buffer in persistence service: {Message}", e.Message);
                return 0;
            }
        }

        public async Task CreateTaskAsync(string uploadId)
        {
            var task = new ServiceDefault.Models.ProcessingTask
            {
                UploadId = uploadId,
                Status = ServiceDefault.Models.ProcessingStatus.Pending,
                Inserted = 0,
                StartedAt = DateTime.UtcNow
            };

            await _repository.CreateTaskAsync(task);
        }

        public async Task IncrementTaskProgressAsync(string uploadId, int delta)
        {
            await _repository.IncrementTaskInsertedAsync(uploadId, delta);
        }

        public async Task UpdateTaskStatusAsync(string uploadId, ServiceDefault.Models.ProcessingStatus status, string? message = null)
        {
            await _repository.UpdateTaskStatusAsync(uploadId, status, message);
        }

        public async Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus status)
        {
            return await _repository.GetTasksByStatusAsync(status);
        }
    }
}
