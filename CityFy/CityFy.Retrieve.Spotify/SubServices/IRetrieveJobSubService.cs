using CityFy.Retrieve.Spotify.Models;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

/// <summary>Retrieve jobs: task status ('spotify_retrieve_tasks'), in-memory queue and cancellation.</summary>
public interface IRetrieveJobSubService
{
    /// <summary>Pending task + job in the background queue. Returns the job id.</summary>
    Task<string> CreateAndEnqueueAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Marks the job InProgress and registers it as cancellable. Null when it was cancelled or
    /// deleted while queued (nothing to do).
    /// </summary>
    Task<CancellationTokenSource?> TryStartAsync(RetrieveJob job, CancellationToken stoppingToken);

    Task CompleteAsync(string jobId, int inserted);

    Task FailAsync(string jobId, string message, string errorCode);

    /// <summary>The job is no longer running (always call after <see cref="TryStartAsync"/>).</summary>
    void End(string jobId, CancellationTokenSource cts);

    /// <summary>
    /// Queued -> Cancelled (never starts); running -> Cancelled and it stops at the next step.
    /// Throws RETRIEVE_JOB_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409). Returns the status it had.
    /// </summary>
    Task<ProcessingStatus> CancelAsync(string jobId, CancellationToken ct);

    /// <summary>Cancels every queued/running job of the user (finished meanwhile is fine). Returns how many.</summary>
    Task<int> CancelAllActiveAsync(string userId, CancellationToken ct);

    Task<ProcessingTask?> GetAsync(string jobId, CancellationToken ct);

    /// <summary>Most recent first, every user.</summary>
    Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct);

    /// <summary>Every job of the user. Returns how many.</summary>
    Task<long> DeleteAllAsync(string userId, CancellationToken ct);
}