using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Repositories
{
    // Retrieve job tracking. Plays are written through the shared IListeningEventStore.
    public interface ISpotifyRepository
    {
        Task CreateTaskAsync(string jobId, string userId, CancellationToken ct);
        Task UpdateTaskAsync(string jobId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null);
        Task<ProcessingTask?> GetTaskAsync(string jobId, CancellationToken ct);

        /// <summary>Progress of a running job (admin panel): percent may be null (indeterminate).</summary>
        Task UpdateProgressAsync(string jobId, double? percent, string? detail, int inserted);

        /// <summary>Most recent jobs first.</summary>
        Task<IReadOnlyList<ProcessingTask>> ListTasksAsync(int limit, CancellationToken ct);

        /// <summary>Jobs of a user still Pending or running.</summary>
        Task<IReadOnlyList<ProcessingTask>> ListActiveTasksAsync(string userId, CancellationToken ct);

        /// <summary>Every job of a user (data deletion). Returns how many.</summary>
        Task<long> DeleteTasksAsync(string userId, CancellationToken ct);
    }
}
