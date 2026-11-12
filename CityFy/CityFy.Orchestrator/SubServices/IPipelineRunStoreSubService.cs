using CityFy.Orchestrator.Models;
using ServiceDefault.Models;

namespace CityFy.Orchestrator.SubServices;

/// <summary>Pipeline runs ('pipeline_runs') and their state transitions.</summary>
public interface IPipelineRunStoreSubService
{
    Task<PipelineRun?> FindPendingAsync(string userId, CancellationToken ct);

    /// <summary>Adds the trigger to a still-Pending run and moves its start. Null when it started meanwhile.</summary>
    Task<PipelineRun?> TryAppendTriggerAsync(string runId, PipelineTrigger trigger, DateTime dueAt, CancellationToken ct);

    /// <summary>New Pending run for the configured periods.</summary>
    Task<PipelineRun> CreateAsync(string userId, PipelineTrigger trigger, DateTime dueAt, CancellationToken ct);

    /// <summary>Due Pending runs switched to Building (a user's builds never overlap).</summary>
    Task<IReadOnlyList<PipelineRun>> ClaimDueAsync(DateTime now, CancellationToken ct);

    /// <summary>Back to Pending, retried at <paramref name="dueAt"/>.</summary>
    Task ReleaseAsync(string runId, DateTime dueAt, string error, CancellationToken ct);

    /// <summary>Building runs without answer within Pipeline:BuildTimeoutMinutes -> Failed. Returns how many.</summary>
    Task<long> FailStuckAsync(DateTime now, CancellationToken ct);

    /// <summary>Closes a Building run. False when it was already closed (timed out, cancelled).</summary>
    Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, CancellationToken ct);

    /// <summary>Throws PIPELINE_RUN_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409). Returns the cancelled run.</summary>
    Task<PipelineRun> CancelAsync(string runId, CancellationToken ct);

    Task<PipelineRun?> GetAsync(string runId, CancellationToken ct);

    Task<IReadOnlyList<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct);

    /// <summary>Every run of the user (Pending ones never start).</summary>
    Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct);
}
