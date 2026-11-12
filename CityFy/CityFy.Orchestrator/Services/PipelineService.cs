using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.Repositories;
using Microsoft.Extensions.Options;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.Orchestrator.Services;

public sealed class PipelineService(
    IPipelineRunRepository runs,
    IListeningUserRepository listeningUsers,
    IMessagePublisher publisher,
    IOptions<PipelineOptions> options,
    ILogger<PipelineService> logger) : IPipelineService
{
    private const string CatalogSource = "catalog";
    private const string ManualSource = "manual";
    private const int MinRetryDelaySeconds = 10;

    private readonly PipelineOptions _options = options.Value;

    private TimeSpan Debounce => TimeSpan.FromSeconds(_options.DebounceSeconds);
    private TimeSpan MaxWait => TimeSpan.FromSeconds(_options.MaxWaitSeconds);

    public async Task<PipelineRun?> AddImportAsync(ListeningImported message, CancellationToken ct)
    {
        if (message.NewPlays <= 0)
            return null; // nothing new: the city would not change

        var run = await runs.AddTriggerAsync(
            message.UserId,
            new PipelineTrigger { Source = message.Source, SourceRef = message.SourceRef, NewPlays = message.NewPlays, At = DateTime.UtcNow },
            _options.Periods, Debounce, MaxWait, ct);

        logger.LogInformation("User {UserId}: {NewPlays} new plays from {Source} -> run {RunId} due at {DueAt:O}",
            message.UserId, message.NewPlays, message.Source, run.RunId, run.DueAt);
        return run;
    }

    public async Task<int> RebuildAllAsync(CatalogUpdated message, CancellationToken ct)
    {
        var users = await listeningUsers.GetUserIdsWithPlaysAsync(ct);
        foreach (var userId in users)
            await runs.AddTriggerAsync(userId,
                new PipelineTrigger { Source = CatalogSource, SourceRef = message.DumpName ?? "musicbrainz", NewPlays = 0, At = DateTime.UtcNow },
                _options.Periods, Debounce, MaxWait, ct);

        logger.LogInformation("Genre catalog {Dump} updated: rebuild queued for {Count} users", message.DumpName, users.Count);
        return users.Count;
    }

    public Task<PipelineRun> TriggerAsync(string userId, CancellationToken ct) =>
        runs.AddTriggerAsync(userId,
            new PipelineTrigger { Source = ManualSource, SourceRef = "admin", NewPlays = 0, At = DateTime.UtcNow },
            _options.Periods, TimeSpan.Zero, TimeSpan.Zero, ct);

    public async Task StartDueRunsAsync(DateTime now, CancellationToken ct)
    {
        var stuck = await runs.FailStuckAsync(now.AddMinutes(-_options.BuildTimeoutMinutes), now, ct);
        if (stuck > 0)
            logger.LogWarning("{Count} pipeline runs timed out", stuck);

        foreach (var run in await runs.ClaimDueRunsAsync(now, ct))
        {
            try
            {
                await publisher.PublishAsync(new BuildCityRequested(run.RunId, run.UserId, run.Periods), correlationId: run.RunId, ct);
                logger.LogInformation("Run {RunId}: city build requested for {UserId} ({Triggers} imports)", run.RunId, run.UserId, run.Triggers.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Run {RunId}: cannot publish BuildCityRequested, retrying later", run.RunId);
                await runs.ReleaseAsync(run.RunId, now.AddSeconds(Math.Max(_options.DebounceSeconds, MinRetryDelaySeconds)), ex.Message, ct);
            }
        }
    }

    public Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, CancellationToken ct) =>
        runs.CompleteAsync(runId, status, errorCode, error, DateTime.UtcNow, ct);

    public async Task<PipelineRun> CancelAsync(string runId, CancellationToken ct)
    {
        _ = await runs.GetAsync(runId, ct) ?? throw NotFound(runId);

        if (!await runs.CancelAsync(runId, DateTime.UtcNow, ct))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Pipeline run '{runId}' is not active", StatusCodes.Status409Conflict);

        logger.LogInformation("Run {RunId} cancelled by an administrator", runId);
        return await runs.GetAsync(runId, ct) ?? throw NotFound(runId);
    }

    public Task<PipelineRun?> GetAsync(string runId, CancellationToken ct) => runs.GetAsync(runId, ct);

    public Task<IReadOnlyList<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct) => runs.ListAsync(userId, limit, ct);

    public async Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        new("Orchestrator", new Dictionary<string, long> { ["pipelineRuns"] = await runs.DeleteUserAsync(userId, ct) });

    private static CityFyException NotFound(string runId) =>
        new(ErrorCodes.PipelineRunNotFound, $"Pipeline run '{runId}' not found", StatusCodes.Status404NotFound);
}
