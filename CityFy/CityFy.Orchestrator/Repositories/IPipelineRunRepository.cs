using CityFy.Orchestrator.Models;

namespace CityFy.Orchestrator.Repositories;

/// <summary>'pipeline_runs' collection.</summary>
public interface IPipelineRunRepository
{
    /// <summary>The user's Pending run, if any.</summary>
    Task<PipelineRun?> FindPendingAsync(string userId, CancellationToken ct);

    /// <summary>Adds a trigger and moves the start of a still-Pending run. Null when it started meanwhile.</summary>
    Task<PipelineRun?> TryAppendTriggerAsync(string runId, PipelineTrigger trigger, DateTime dueAt, CancellationToken ct);

    Task InsertAsync(PipelineRun run, CancellationToken ct);

    /// <summary>
    /// Pending runs whose debounce is over, switched to Building. A user's run waits while
    /// another run of the same user is still Building (builds of a user never overlap).
    /// </summary>
    Task<IReadOnlyList<PipelineRun>> ClaimDueRunsAsync(DateTime now, CancellationToken ct);

    /// <summary>Back to Pending (e.g. the command could not be published): retried at the next poll after the delay.</summary>
    Task ReleaseAsync(string runId, DateTime dueAt, string error, CancellationToken ct);

    /// <summary>Building -> Done/Failed. Late answers for a run already closed (e.g. timed out) are ignored.</summary>
    Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, DateTime now, CancellationToken ct);

    /// <summary>Pending/Building -> Cancelled. False when the run is not active anymore.</summary>
    Task<bool> CancelAsync(string runId, DateTime now, CancellationToken ct);

    /// <summary>Building runs started before <paramref name="startedBefore"/> -> Failed (PIPELINE_TIMEOUT). Returns how many.</summary>
    Task<long> FailStuckAsync(DateTime startedBefore, DateTime now, CancellationToken ct);

    /// <summary>Every run of a user (data deletion). Returns how many.</summary>
    Task<long> DeleteUserAsync(string userId, CancellationToken ct);

    Task<PipelineRun?> GetAsync(string runId, CancellationToken ct);

    /// <summary>Most recent first; every user when <paramref name="userId"/> is null.</summary>
    Task<IReadOnlyList<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct);
}
