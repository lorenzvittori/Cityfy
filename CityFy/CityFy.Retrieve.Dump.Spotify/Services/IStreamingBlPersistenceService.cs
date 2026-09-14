using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public interface IStreamingBlPersistenceService
    {
        Task EnsureCollectionExistsAsync(string collectionName);
        Task<int> PersistBufferAsync(string collectionName, IEnumerable<StreamingBl> buffer, string uploadId);
        Task CreateTaskAsync(string uploadId);
        Task IncrementTaskProgressAsync(string uploadId, int delta);
        Task UpdateTaskStatusAsync(string uploadId, ServiceDefault.Models.ProcessingStatus status, string? message = null);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus status);
    }
}
