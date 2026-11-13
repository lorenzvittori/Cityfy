using CityFy.Retrieve.LastFm.Models;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>Lifecycle of the history imports: task document, queue, cancellation.</summary>
public interface ILastFmImportJobSubService
{
    /// <summary>Id of a new import (the account records it before the import is queued).</summary>
    string NewImportId();

    Task CreateAndEnqueueAsync(string importId, string userId, CancellationToken ct);

    /// <summary>Null when the import was cancelled or deleted while queued; otherwise InProgress and its token.</summary>
    Task<CancellationTokenSource?> TryStartAsync(LastFmImportJob job, CancellationToken stoppingToken);

    Task ProgressAsync(string importId, int page, int totalPages, int inserted);

    Task CompleteAsync(string importId, int inserted);

    Task FailAsync(string importId, string message, string errorCode);

    void End(string importId, CancellationTokenSource cts);

    /// <summary>Throws LASTFM_IMPORT_NOT_FOUND (404), PROCESS_NOT_CANCELLABLE (409) when already finished.</summary>
    Task<ProcessingStatus> CancelAsync(string importId, CancellationToken ct);

    Task<int> CancelAllActiveAsync(string userId, CancellationToken ct);

    /// <summary>Queues again the imports left Pending or InProgress (startup).</summary>
    Task<int> RequeueUnfinishedAsync(CancellationToken ct);

    Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct);

    Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct);

    Task<long> DeleteAllAsync(string userId, CancellationToken ct);

    /// <summary>Stops every running or queued import of every user. Returns how many were stopped.</summary>
    Task<int> CancelEverythingAsync(CancellationToken ct);

    Task<long> DeleteEverythingAsync(CancellationToken ct);
}
