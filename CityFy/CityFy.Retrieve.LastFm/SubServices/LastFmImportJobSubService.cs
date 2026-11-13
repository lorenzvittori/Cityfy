using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Repositories;
using CityFy.Retrieve.LastFm.Services;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmImportJobSubService(ILastFmImportTaskRepository tasks, ILastFmImportQueue queue, CancellationRegistry running)
    : ILastFmImportJobSubService
{
    public const int MaxListLimit = 200;

    public string NewImportId() => Guid.NewGuid().ToString("N");

    public async Task CreateAndEnqueueAsync(string importId, string userId, CancellationToken ct)
    {
        await tasks.CreateAsync(importId, userId, ct);
        await queue.EnqueueAsync(new LastFmImportJob(importId, userId), ct);
    }

    public async Task<CancellationTokenSource?> TryStartAsync(LastFmImportJob job, CancellationToken stoppingToken)
    {
        if (await tasks.GetAsync(job.ImportId, stoppingToken) is not { } task ||
            task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            return null;

        var cts = running.Register(job.ImportId, stoppingToken);
        await tasks.UpdateStatusAsync(job.ImportId, ProcessingStatus.InProgress);
        return cts;
    }

    public Task ProgressAsync(string importId, int page, int totalPages, int inserted) =>
        tasks.UpdateProgressAsync(importId,
            totalPages > 0 ? Math.Round(100.0 * page / totalPages, 1) : null,
            $"Page {page} of {totalPages} · {inserted} new plays",
            inserted);

    public Task CompleteAsync(string importId, int inserted) => tasks.UpdateStatusAsync(importId, ProcessingStatus.Done, inserted);

    public Task FailAsync(string importId, string message, string errorCode) =>
        tasks.UpdateStatusAsync(importId, ProcessingStatus.Failed, message: message, errorCode: errorCode);

    public void End(string importId, CancellationTokenSource cts) => running.Unregister(importId, cts);

    public async Task<ProcessingStatus> CancelAsync(string importId, CancellationToken ct)
    {
        var task = await tasks.GetAsync(importId, ct)
            ?? throw new CityFyException(ErrorCodes.LastFmImportNotFound, $"Last.fm import '{importId}' not found", StatusCodes.Status404NotFound);
        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Last.fm import '{importId}' is {task.Status}", StatusCodes.Status409Conflict);

        // Status first (a queued import is skipped), then stop the running one
        await tasks.UpdateStatusAsync(importId, ProcessingStatus.Cancelled, message: "Cancelled");
        running.Cancel(importId);
        return task.Status;
    }

    public async Task<int> CancelAllActiveAsync(string userId, CancellationToken ct)
    {
        var cancelled = 0;
        foreach (var task in await tasks.ListUnfinishedAsync(userId, ct))
        {
            try
            {
                await CancelAsync(task.UploadId, ct);
                cancelled++;
            }
            catch (CityFyException e) when (e.Code is ErrorCodes.ProcessNotCancellable or ErrorCodes.LastFmImportNotFound)
            {
                // finished meanwhile
            }
        }
        return cancelled;
    }

    public async Task<int> RequeueUnfinishedAsync(CancellationToken ct)
    {
        var unfinished = await tasks.ListUnfinishedAsync(null, ct);
        foreach (var task in unfinished)
            await queue.EnqueueAsync(new LastFmImportJob(task.UploadId, task.UserId!), ct);
        return unfinished.Count;
    }

    public Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct) => tasks.GetAsync(importId, ct);

    public Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct) =>
        tasks.ListAsync(Math.Clamp(limit, 1, MaxListLimit), ct);

    public Task<long> DeleteAllAsync(string userId, CancellationToken ct) => tasks.DeleteAllAsync(userId, ct);

    public async Task<int> CancelEverythingAsync(CancellationToken ct)
    {
        var stopped = 0;
        foreach (var task in await tasks.ListUnfinishedAsync(null, ct))
        {
            await tasks.UpdateStatusAsync(task.UploadId, ProcessingStatus.Cancelled, message: "Last.fm data deleted");
            running.Cancel(task.UploadId);
            stopped++;
        }
        return stopped;
    }

    public Task<long> DeleteEverythingAsync(CancellationToken ct) => tasks.DeleteEverythingAsync(ct);
}
