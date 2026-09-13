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
        Task UpdateTaskStatusAsync(string uploadId, string status, string? message = null);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(string status);
    }
}
