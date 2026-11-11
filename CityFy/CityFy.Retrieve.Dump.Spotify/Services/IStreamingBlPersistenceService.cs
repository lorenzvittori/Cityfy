using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public interface IStreamingBlPersistenceService
    {
        /// <summary>
        /// Maps a batch of dump entries to listening events, upserts them in 'listening_events'
        /// and adds the number of NEW plays to the task counter. Returns that number.
        /// </summary>
        Task<int> PersistBufferAsync(string userId, IReadOnlyList<StreamingBl> buffer, string uploadId, CancellationToken ct = default);

        Task CreateTaskAsync(string uploadId, string userId);

        /// <summary>Progress of the import, shown in the admin panel. Never fails the import.</summary>
        Task ReportProgressAsync(string uploadId, double? percent, string? detail);
        Task UpdateTaskStatusAsync(string uploadId, ServiceDefault.Models.ProcessingStatus status, string? message = null);
        Task<IEnumerable<ServiceDefault.Models.ProcessingTask>> GetTasksByStatusAsync(ServiceDefault.Models.ProcessingStatus status);

        /// <summary>
        /// Copies a legacy per-upload collection into 'listening_events' for the given user.
        /// Idempotent: can be re-run safely. Returns the number of new plays.
        /// </summary>
        Task<int> MigrateLegacyUploadAsync(string uploadId, string userId, CancellationToken ct = default);
    }
}
