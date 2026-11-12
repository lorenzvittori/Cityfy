using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.Repositories;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Orchestrator.SubServices;

public sealed class PipelineRunStoreSubService(IPipelineRunRepository runs, IOptions<PipelineOptions> options) : IPipelineRunStoreSubService
{
    public const string ServiceName = "Orchestrator";
    public const int MaxListLimit = 200;

    private readonly PipelineOptions _options = options.Value;

    public Task<PipelineRun?> FindPendingAsync(string userId, CancellationToken ct) => runs.FindPendingAsync(userId, ct);

    public Task<PipelineRun?> TryAppendTriggerAsync(string runId, PipelineTrigger trigger, DateTime dueAt, CancellationToken ct) =>
        runs.TryAppendTriggerAsync(runId, trigger, dueAt, ct);

    public async Task<PipelineRun> CreateAsync(string userId, PipelineTrigger trigger, DateTime dueAt, CancellationToken ct)
    {
        var run = new PipelineRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Status = PipelineStatus.Pending,
            Triggers = [trigger],
            Periods = _options.Periods.ToList(),
            CreatedAt = trigger.At,
            DueAt = dueAt
        };
        await runs.InsertAsync(run, ct);
        return run;
    }

    public Task<IReadOnlyList<PipelineRun>> ClaimDueAsync(DateTime now, CancellationToken ct) => runs.ClaimDueRunsAsync(now, ct);

    public Task ReleaseAsync(string runId, DateTime dueAt, string error, CancellationToken ct) => runs.ReleaseAsync(runId, dueAt, error, ct);

    public Task<long> FailStuckAsync(DateTime now, CancellationToken ct) =>
        runs.FailStuckAsync(now.AddMinutes(-_options.BuildTimeoutMinutes), now, ct);

    public Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, CancellationToken ct) =>
        runs.CompleteAsync(runId, status, errorCode, error, DateTime.UtcNow, ct);

    public async Task<PipelineRun> CancelAsync(string runId, CancellationToken ct)
    {
        _ = await runs.GetAsync(runId, ct) ?? throw NotFound(runId);

        if (!await runs.CancelAsync(runId, DateTime.UtcNow, ct))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Pipeline run '{runId}' is not active", StatusCodes.Status409Conflict);

        return await runs.GetAsync(runId, ct) ?? throw NotFound(runId);
    }

    public Task<PipelineRun?> GetAsync(string runId, CancellationToken ct) => runs.GetAsync(runId, ct);

    public Task<IReadOnlyList<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct) =>
        runs.ListAsync(userId, Math.Clamp(limit, 1, MaxListLimit), ct);

    public async Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        new(ServiceName, new Dictionary<string, long> { ["pipelineRuns"] = await runs.DeleteUserAsync(userId, ct) });

    private static CityFyException NotFound(string runId) =>
        new(ErrorCodes.PipelineRunNotFound, $"Pipeline run '{runId}' not found", StatusCodes.Status404NotFound);
}
