using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Repositories
{
    public interface IStreamingRepository
    {
        // Task management for processing lifecycle
        Task CreateTaskAsync(ServiceDefault.Models.ProcessingTask task);
        Task IncrementTaskInsertedAsync(string uploadId, int delta);
        Task UpdateTaskStatusAsync(string uploadId, ServiceDefault.Models.ProcessingStatus status, string? message = null, string? errorCode = null);
        Task UpdateTaskProgressAsync(string uploadId, double? percent, string? detail);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus status);

        // Read operations
        Task<ServiceDefault.Models.ProcessingTask?> GetTaskByUploadIdAsync(string uploadId);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksPagedAsync(int page, int pageSize);

        /// <summary>
        /// Recupera documenti (raw) dalla collection specificata con paginazione. Se providedUploadId è impostato filtra per _uploadId.
        /// </summary>
        Task<IEnumerable<string>> GetDocumentsPagedAsync(string collectionName, int page, int pageSize, string? providedUploadId = null);

        /// <summary>
        /// Reads, in batches, a legacy per-upload collection ("{CollectionPrefix}{uploadId}")
        /// written before the switch to 'listening_events'. Used only by the migration.
        /// </summary>
        IAsyncEnumerable<IReadOnlyList<StreamingBl>> ReadLegacyUploadAsync(string uploadId, int batchSize, CancellationToken ct);
    }
}
