using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Repositories
{
    // Retrieve job tracking. Plays are written through the shared IListeningEventStore.
    public interface ISpotifyRepository
    {
        Task CreateTaskAsync(string jobId, string userId, CancellationToken ct);
        Task UpdateTaskAsync(string jobId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null);
        Task<ProcessingTask?> GetTaskAsync(string jobId, CancellationToken ct);
    }
}
