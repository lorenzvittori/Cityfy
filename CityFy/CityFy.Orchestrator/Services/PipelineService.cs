using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.SubServices;
using ServiceDefault.Contracts;
using ServiceDefault.Models;

namespace CityFy.Orchestrator.Services;

public sealed class PipelineService(
    IPipelineTriggerSubService triggers,
    IPipelineDebounceSubService debounce,
    IPipelineRunStoreSubService runs,
    IListeningUsersSubService listeningUsers,
    IBuildRequestSubService buildRequests,
    ILogger<PipelineService> logger) : IPipelineService
{
    public async Task<PipelineRun?> AddImportAsync(ListeningImported message, CancellationToken ct)
    {
        if (triggers.FromImport(message) is not { } trigger)
            return null;

        var run = await AddTriggerAsync(message.UserId, trigger, immediate: false, ct);
        logger.LogInformation("User {UserId}: {NewPlays} new plays from {Source} -> run {RunId} due at {DueAt:O}",
            message.UserId, message.NewPlays, message.Source, run.RunId, run.DueAt);
        return run;
    }

    public async Task<int> RebuildAllAsync(CatalogUpdated message, CancellationToken ct)
    {
        var users = await listeningUsers.GetUserIdsWithPlaysAsync(ct);
        foreach (var userId in users)
            await AddTriggerAsync(userId, triggers.FromCatalog(message), immediate: false, ct);

        logger.LogInformation("Genre catalog {Dump} updated: rebuild queued for {Count} users", message.DumpName, users.Count);
        return users.Count;
    }

    public Task<PipelineRun> TriggerAsync(string userId, CancellationToken ct) =>
        AddTriggerAsync(userId, triggers.Manual(), immediate: true, ct);

    public async Task StartDueRunsAsync(DateTime now, CancellationToken ct)
    {
        var stuck = await runs.FailStuckAsync(now, ct);
        if (stuck > 0)
            logger.LogWarning("{Count} pipeline runs timed out", stuck);

        foreach (var run in await runs.ClaimDueAsync(now, ct))
        {
            try
            {
                await buildRequests.PublishAsync(run, ct);
                logger.LogInformation("Run {RunId}: city build requested for {UserId} ({Triggers} imports)", run.RunId, run.UserId, run.Triggers.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Run {RunId}: cannot publish BuildCityRequested, retrying later", run.RunId);
                await runs.ReleaseAsync(run.RunId, debounce.RetryAt(now), ex.Message, ct);
            }
        }
    }

    public Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, CancellationToken ct) =>
        runs.CompleteAsync(runId, status, errorCode, error, ct);

    public async Task<PipelineRun> CancelAsync(string runId, CancellationToken ct)
    {
        var run = await runs.CancelAsync(runId, ct);
        logger.LogInformation("Run {RunId} cancelled by an administrator", runId);
        return run;
    }

    public Task<PipelineRun?> GetAsync(string runId, CancellationToken ct) => runs.GetAsync(runId, ct);

    public Task<IReadOnlyList<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct) => runs.ListAsync(userId, limit, ct);

    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) => runs.DeleteUserDataAsync(userId, ct);

    /// <summary>
    /// Joins the user's Pending run (pushing its start by the debounce) or opens a new one.
    /// Called by a single consumer (prefetch 1), so two Pending runs for a user cannot appear.
    /// </summary>
    private async Task<PipelineRun> AddTriggerAsync(string userId, PipelineTrigger trigger, bool immediate, CancellationToken ct)
    {
        if (await runs.FindPendingAsync(userId, ct) is { } pending &&
            await runs.TryAppendTriggerAsync(pending.RunId, trigger, debounce.DueAt(pending.CreatedAt, trigger.At, immediate), ct) is { } extended)
            return extended;

        // No Pending run, or it started meanwhile: open a new one
        return await runs.CreateAsync(userId, trigger, debounce.DueAt(trigger.At, trigger.At, immediate), ct);
    }
}
