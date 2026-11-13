using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Repositories;

/// <summary>'lastfm_import_tasks': status of the history imports (shared ProcessingTask shape).</summary>
public interface ILastFmImportTaskRepository
{
    Task CreateAsync(string importId, string userId, CancellationToken ct);

    Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct);

    Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct);

    /// <summary>Pending or InProgress imports of a user (null = of everybody: recovery at startup).</summary>
    Task<IReadOnlyList<ProcessingTask>> ListUnfinishedAsync(string? userId, CancellationToken ct);

    /// <summary>
    /// Status change; a Cancelled import is never overwritten by a late Done/Failed. Always lands, even
    /// when the job itself was cancelled.
    /// </summary>
    Task UpdateStatusAsync(string importId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null);

    Task UpdateProgressAsync(string importId, double? percent, string? detail, int inserted);

    Task<long> DeleteAllAsync(string userId, CancellationToken ct);

    /// <summary>Every import of every user (end of the Last.fm licence).</summary>
    Task<long> DeleteEverythingAsync(CancellationToken ct);
}
