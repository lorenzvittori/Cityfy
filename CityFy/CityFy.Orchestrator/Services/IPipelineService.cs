using CityFy.Orchestrator.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Models;

namespace CityFy.Orchestrator.Services;

/// <summary>Pipeline runs: one refresh of the artist catalog per user, debounced over the imports that triggered it.</summary>
public interface IPipelineService
{
    /// <summary>New plays: opens or extends (debounce) the user's Pending run. Null when nothing new was imported.</summary>
    Task<PipelineRun?> AddImportAsync(ListeningImported message, CancellationToken ct);

    /// <summary>New genre catalog: queues a catalog refresh for every user with plays (normal debounce). Returns how many users.</summary>
    Task<int> RebuildAllAsync(CatalogUpdated message, CancellationToken ct);

    /// <summary>Refresh a user's artist catalog now, without importing anything (no debounce).</summary>
    Task<PipelineRun> TriggerAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Starts the Pending runs whose debounce is over (publishes RefreshCatalogRequested) and fails the
    /// Building runs that got no answer within the timeout.
    /// </summary>
    Task StartDueRunsAsync(DateTime now, CancellationToken ct);

    /// <summary>Closes a Building run. False when the run was already closed (timed out, cancelled).</summary>
    Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, CancellationToken ct);

    /// <summary>
    /// Admin: a Pending run never starts, a Building one is closed (Catalog finishes the refresh in
    /// progress, its answer is ignored). Throws PIPELINE_RUN_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409).
    /// </summary>
    Task<PipelineRun> CancelAsync(string runId, CancellationToken ct);

    Task<PipelineRun?> GetAsync(string runId, CancellationToken ct);

    Task<IReadOnlyList<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct);

    /// <summary>Every pipeline run of the user (Pending ones never start).</summary>
    Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct);
}
