using CityFy.Orchestrator.Models;
using CityFy.Orchestrator.Services;
using MongoDB.Driver;

namespace CityFy.Orchestrator.Repositories;

public class PipelineRunRepository
{
    private readonly IMongoCollection<PipelineRun> _runs;
    private readonly Lazy<Task> _indexes;

    public PipelineRunRepository(IMongoDatabase db)
    {
        _runs = db.GetCollection<PipelineRun>("pipeline_runs");
        _indexes = new Lazy<Task>(() => _runs.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<PipelineRun>(Builders<PipelineRun>.IndexKeys.Ascending(r => r.UserId).Descending(r => r.CreatedAt)),
            new CreateIndexModel<PipelineRun>(Builders<PipelineRun>.IndexKeys.Ascending(r => r.Status).Ascending(r => r.DueAt)),
        ]));
    }

    /// <summary>
    /// Joins the user's Pending run (pushing its start by the debounce) or opens a new one.
    /// Called by a single consumer (prefetch 1), so two Pending runs for a user cannot appear.
    /// </summary>
    public async Task<PipelineRun> AddTriggerAsync(string userId, PipelineTrigger trigger, IReadOnlyList<string> periods,
        TimeSpan debounce, TimeSpan maxWait, CancellationToken ct)
    {
        await _indexes.Value;
        var now = trigger.At;

        var pending = await _runs.Find(r => r.UserId == userId && r.Status == PipelineStatus.Pending).FirstOrDefaultAsync(ct);
        if (pending != null)
        {
            var dueAt = PipelinePolicy.ComputeDueAt(pending.CreatedAt, now, debounce, maxWait);
            var updated = await _runs.FindOneAndUpdateAsync<PipelineRun>(
                r => r.RunId == pending.RunId && r.Status == PipelineStatus.Pending,
                Builders<PipelineRun>.Update.Push(r => r.Triggers, trigger).Set(r => r.DueAt, dueAt),
                new FindOneAndUpdateOptions<PipelineRun> { ReturnDocument = ReturnDocument.After },
                ct);
            if (updated != null)
                return updated;
            // started in the meantime: open a new run
        }

        var run = new PipelineRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Status = PipelineStatus.Pending,
            Triggers = [trigger],
            Periods = periods.ToList(),
            CreatedAt = now,
            DueAt = PipelinePolicy.ComputeDueAt(now, now, debounce, maxWait)
        };
        await _runs.InsertOneAsync(run, cancellationToken: ct);
        return run;
    }

    /// <summary>
    /// Pending runs whose debounce is over, switched to Building. A user's run waits while
    /// another run of the same user is still Building (builds of a user never overlap).
    /// </summary>
    public async Task<List<PipelineRun>> ClaimDueRunsAsync(DateTime now, CancellationToken ct)
    {
        await _indexes.Value;

        var due = await _runs.Find(r => r.Status == PipelineStatus.Pending && r.DueAt <= now)
            .SortBy(r => r.DueAt).Limit(50).ToListAsync(ct);
        if (due.Count == 0) return [];

        var dueUsers = due.Select(r => r.UserId).Distinct().ToList();
        var busyUsers = (await _runs.Find(r => r.Status == PipelineStatus.Building && dueUsers.Contains(r.UserId))
            .Project(r => r.UserId).ToListAsync(ct)).ToHashSet();

        var claimed = new List<PipelineRun>();
        foreach (var run in due.Where(r => !busyUsers.Contains(r.UserId)))
        {
            var started = await _runs.FindOneAndUpdateAsync<PipelineRun>(
                r => r.RunId == run.RunId && r.Status == PipelineStatus.Pending,
                Builders<PipelineRun>.Update.Set(r => r.Status, PipelineStatus.Building).Set(r => r.StartedAt, now),
                new FindOneAndUpdateOptions<PipelineRun> { ReturnDocument = ReturnDocument.After },
                ct);
            if (started != null)
                claimed.Add(started);
        }
        return claimed;
    }

    /// <summary>Back to Pending (e.g. the command could not be published): retried at the next poll after the delay.</summary>
    public Task ReleaseAsync(string runId, DateTime dueAt, string error, CancellationToken ct) =>
        _runs.UpdateOneAsync(r => r.RunId == runId && r.Status == PipelineStatus.Building,
            Builders<PipelineRun>.Update.Set(r => r.Status, PipelineStatus.Pending).Set(r => r.DueAt, dueAt)
                .Set(r => r.StartedAt, null).Set(r => r.Error, error),
            cancellationToken: ct);

    /// <summary>Building -> Done/Failed. Late answers for a run already closed (e.g. timed out) are ignored.</summary>
    public async Task<bool> CompleteAsync(string runId, PipelineStatus status, string? errorCode, string? error, DateTime now, CancellationToken ct)
    {
        var result = await _runs.UpdateOneAsync(r => r.RunId == runId && r.Status == PipelineStatus.Building,
            Builders<PipelineRun>.Update.Set(r => r.Status, status).Set(r => r.CompletedAt, now)
                .Set(r => r.Error, error).Set(r => r.ErrorCode, errorCode),
            cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Pending/Building -> Cancelled. A Pending run never starts; for a Building run the late
    /// CityBuilt/CityBuildFailed is ignored (CompleteAsync only closes Building runs).
    /// False when the run is not active anymore.
    /// </summary>
    public async Task<bool> CancelAsync(string runId, DateTime now, CancellationToken ct)
    {
        var result = await _runs.UpdateOneAsync(
            r => r.RunId == runId && (r.Status == PipelineStatus.Pending || r.Status == PipelineStatus.Building),
            Builders<PipelineRun>.Update.Set(r => r.Status, PipelineStatus.Cancelled).Set(r => r.CompletedAt, now)
                .Set(r => r.Error, "Annullato dall'amministratore")
                .Set(r => r.ErrorCode, ServiceDefault.Errors.ErrorCodes.ProcessCancelled),
            cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<long> FailStuckAsync(DateTime startedBefore, DateTime now, CancellationToken ct)
    {
        var result = await _runs.UpdateManyAsync(r => r.Status == PipelineStatus.Building && r.StartedAt < startedBefore,
            Builders<PipelineRun>.Update.Set(r => r.Status, PipelineStatus.Failed).Set(r => r.CompletedAt, now)
                .Set(r => r.Error, "Timeout: no answer from Elaboration")
                .Set(r => r.ErrorCode, ServiceDefault.Errors.ErrorCodes.PipelineTimeout),
            cancellationToken: ct);
        return result.ModifiedCount;
    }

    /// <summary>
    /// Every run of a user (data deletion): Pending runs never start, a Building one is forgotten
    /// (its late CityBuilt/CityBuildFailed finds nothing to close). Returns how many.
    /// </summary>
    public async Task<long> DeleteUserAsync(string userId, CancellationToken ct) =>
        (await _runs.DeleteManyAsync(r => r.UserId == userId, ct)).DeletedCount;

    public async Task<PipelineRun?> GetAsync(string runId, CancellationToken ct) =>
        await _runs.Find(r => r.RunId == runId).FirstOrDefaultAsync(ct);

    public async Task<List<PipelineRun>> ListAsync(string? userId, int limit, CancellationToken ct)
    {
        var filter = string.IsNullOrEmpty(userId)
            ? Builders<PipelineRun>.Filter.Empty
            : Builders<PipelineRun>.Filter.Eq(r => r.UserId, userId);
        return await _runs.Find(filter).SortByDescending(r => r.CreatedAt).Limit(limit).ToListAsync(ct);
    }
}
