using CityFy.Retrieve.Spotify.Models;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Services
{
    public interface ISpotifyRetrieveService
    {
        /// <summary>
        /// Registers a Pending task for a linked account and queues it for the background
        /// worker. Returns the job id. Throws <see cref="SpotifyAccountException"/> if the
        /// account is not linked.
        /// </summary>
        Task<string> EnqueueRetrieveAsync(string userId, CancellationToken ct);

        // Executed by the background worker. Returns the number of new plays stored.
        Task<int> RunRetrieveAsync(RetrieveJob job, CancellationToken ct);

        Task<ProcessingTask?> GetJobAsync(string jobId, CancellationToken ct);

        /// <summary>
        /// Admin: queued -> CANCELLED (never starts); running -> CANCELLED and it stops at the next step.
        /// Throws RETRIEVE_JOB_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409).
        /// </summary>
        Task CancelAsync(string jobId, CancellationToken ct);
    }
}
