using CityFy.Retrieve.LastFm.Models;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Services;

/// <summary>Import of the whole Last.fm history of a user (background job, cancellable, resumable).</summary>
public interface ILastFmImportService
{
    /// <summary>
    /// Reads the history page by page from the resume point saved on the account. Stopped by the
    /// service shutting down, the import stays InProgress and the recovery resumes it at startup.
    /// </summary>
    Task RunAsync(LastFmImportJob job, CancellationToken stoppingToken);

    /// <summary>Throws LASTFM_IMPORT_NOT_FOUND, PROCESS_NOT_CANCELLABLE.</summary>
    Task CancelAsync(string importId, CancellationToken ct);

    Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct);

    Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct);

    /// <summary>Startup: queues again the imports left Pending or InProgress.</summary>
    Task<int> RequeueUnfinishedAsync(CancellationToken ct);
}
