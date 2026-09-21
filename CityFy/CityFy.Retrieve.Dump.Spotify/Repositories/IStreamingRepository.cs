using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Repositories
{
    public interface IStreamingRepository
    {
        // Insert a batch of business-layer models into the specified collection.
        Task InsertManyAsync(string collectionName, IEnumerable<StreamingBl> documents, string uploadId);
        Task EnsureCollectionExistsAsync(string collectionName);

        // Task management for processing lifecycle
        Task CreateTaskAsync(ServiceDefault.Models.ProcessingTask task);
        Task IncrementTaskInsertedAsync(string uploadId, int delta);
        Task UpdateTaskStatusAsync(string uploadId, ServiceDefault.Models.ProcessingStatus status, string? message = null);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus status);

        // Read operations
        Task<ServiceDefault.Models.ProcessingTask?> GetTaskByUploadIdAsync(string uploadId);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksPagedAsync(int page, int pageSize);

        /// <summary>
        /// Recupera documenti (raw) dalla collection specificata con paginazione. Se providedUploadId è impostato filtra per _uploadId.
        /// </summary>
        Task<IEnumerable<string>> GetDocumentsPagedAsync(string collectionName, int page, int pageSize, string? providedUploadId = null);
    }
}
