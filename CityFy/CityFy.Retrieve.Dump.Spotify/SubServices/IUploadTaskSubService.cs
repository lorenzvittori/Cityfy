using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

/// <summary>Upload tasks (status, progress, cancellation of the running import).</summary>
public interface IUploadTaskSubService
{
    Task CreateAsync(string uploadId, string userId);

    Task<ProcessingTask?> GetAsync(string uploadId);

    /// <summary>Uploads of every user, most recent first.</summary>
    Task<IReadOnlyList<ProcessingTask>> ListAsync(int page, int pageSize);

    Task<IReadOnlyList<ProcessingTask>> ListByUserAsync(string userId, CancellationToken ct);

    /// <summary>Pending or interrupted uploads that have an owner (to queue again at startup).</summary>
    Task<IReadOnlyList<ProcessingTask>> ListUnfinishedAsync();

    /// <summary>Done, Failed or Cancelled: a (duplicate) message for it has nothing to do.</summary>
    bool IsFinished(ProcessingTask task);

    /// <summary>
    /// Registers the import as cancellable and marks it InProgress. Null when it was cancelled
    /// meanwhile (nothing to do).
    /// </summary>
    Task<CancellationTokenSource?> TryStartAsync(string uploadId, CancellationToken ct);

    Task CompleteAsync(string uploadId);

    Task FailAsync(string uploadId, string message, string errorCode);

    /// <summary>The import is no longer running (always call after <see cref="TryStartAsync"/>).</summary>
    void End(string uploadId, CancellationTokenSource cts);

    /// <summary>
    /// Pending -> Cancelled (never starts); InProgress -> Cancelled and the running import stops.
    /// Throws UPLOAD_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409). Returns the status it had.
    /// </summary>
    Task<ProcessingStatus> CancelAsync(string uploadId);

    /// <summary>Cancels the upload if still queued or running (finished meanwhile is fine). True when cancelled.</summary>
    Task<bool> CancelIfActiveAsync(ProcessingTask task);

    /// <summary>Every upload task of the user. Returns how many.</summary>
    Task<long> DeleteAllAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Finished uploads → new owner. Uploads still importing stay (they write plays for the old
    /// owner): the count of those is returned, and the transfer has to be repeated once they end.
    /// </summary>
    Task<(long Moved, int StillImporting)> TransferAsync(string fromUserId, string toUserId, CancellationToken ct);

    /// <summary>Drops the legacy per-upload collection, if any.</summary>
    Task DropLegacyAsync(string uploadId, CancellationToken ct);
}