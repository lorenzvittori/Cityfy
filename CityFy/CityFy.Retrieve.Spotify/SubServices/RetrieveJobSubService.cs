using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Repositories;
using CityFy.Retrieve.Spotify.Services;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class RetrieveJobSubService(ISpotifyRepository repo, IRetrieveJobQueue queue, CancellationRegistry running) : IRetrieveJobSubService
{
    public const int MaxListLimit = 200;

    public async Task<string> CreateAndEnqueueAsync(string userId, CancellationToken ct)
    {
        var jobId = Guid.NewGuid().ToString("N");
        await repo.CreateTaskAsync(jobId, userId, ct);
        await queue.EnqueueAsync(new RetrieveJob(jobId, userId), ct);
        return jobId;
    }

    public async Task<CancellationTokenSource?> TryStartAsync(RetrieveJob job, CancellationToken stoppingToken)
    {
        // Cancelled while still in the queue, or deleted with the user's data
        if (await repo.GetTaskAsync(job.JobId, stoppingToken) is not { } queued || queued.Status == ProcessingStatus.Cancelled)
            return null;

        var cts = running.Register(job.JobId, stoppingToken);
        await repo.UpdateTaskAsync(job.JobId, ProcessingStatus.InProgress);
        return cts;
    }

    public Task CompleteAsync(string jobId, int inserted) => repo.UpdateTaskAsync(jobId, ProcessingStatus.Done, inserted);

    public Task FailAsync(string jobId, string message, string errorCode) =>
        repo.UpdateTaskAsync(jobId, ProcessingStatus.Failed, message: message, errorCode: errorCode);

    public void End(string jobId, CancellationTokenSource cts) => running.Unregister(jobId, cts);

    public async Task<ProcessingStatus> CancelAsync(string jobId, CancellationToken ct)
    {
        var task = await repo.GetTaskAsync(jobId, ct)
            ?? throw new CityFyException(ErrorCodes.RetrieveJobNotFound, $"Retrieve job '{jobId}' not found", StatusCodes.Status404NotFound);

        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Retrieve job '{jobId}' is {task.Status}", StatusCodes.Status409Conflict);

        // Status first (a queued job will be skipped), then stop the running one if any
        await repo.UpdateTaskAsync(jobId, ProcessingStatus.Cancelled, message: "Cancelled by an administrator");
        running.Cancel(jobId);
        return task.Status;
    }

    public async Task<int> CancelAllActiveAsync(string userId, CancellationToken ct)
    {
        var cancelled = 0;
        foreach (var job in await repo.ListActiveTasksAsync(userId, ct))
        {
            try
            {
                await CancelAsync(job.UploadId, ct);
                cancelled++;
            }
            catch (CityFyException e) when (e.Code is ErrorCodes.ProcessNotCancellable or ErrorCodes.RetrieveJobNotFound)
            {
                // finished meanwhile: nothing to stop
            }
        }
        return cancelled;
    }

    public Task<ProcessingTask?> GetAsync(string jobId, CancellationToken ct) => repo.GetTaskAsync(jobId, ct);

    public Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct) =>
        repo.ListTasksAsync(Math.Clamp(limit, 1, MaxListLimit), ct);

    public Task<long> DeleteAllAsync(string userId, CancellationToken ct) => repo.DeleteTasksAsync(userId, ct);
}