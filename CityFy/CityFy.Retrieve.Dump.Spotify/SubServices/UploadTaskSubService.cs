using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class UploadTaskSubService(IStreamingRepository repo, CancellationRegistry running) : IUploadTaskSubService
{
    public const int MaxPageSize = 200;

    public Task CreateAsync(string uploadId, string userId) =>
        repo.CreateTaskAsync(new ProcessingTask
        {
            UploadId = uploadId,
            UserId = userId,
            Status = ProcessingStatus.Pending,
            StartedAt = DateTime.UtcNow
        });

    public Task<ProcessingTask?> GetAsync(string uploadId) => repo.GetTaskByUploadIdAsync(uploadId);

    public async Task<IReadOnlyList<ProcessingTask>> ListAsync(int page, int pageSize) =>
        (await repo.GetTasksPagedAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize))).ToList();

    public Task<IReadOnlyList<ProcessingTask>> ListByUserAsync(string userId, CancellationToken ct) => repo.GetTasksByUserAsync(userId, ct);

    public async Task<IReadOnlyList<ProcessingTask>> ListUnfinishedAsync() =>
        (await repo.GetTasksByStatusAsync(ProcessingStatus.Pending))
            .Concat(await repo.GetTasksByStatusAsync(ProcessingStatus.InProgress))
            .Where(HasUser)
            .ToList();

    private static bool HasUser(ProcessingTask task) => !string.IsNullOrWhiteSpace(task.UserId);

    public bool IsFinished(ProcessingTask task) =>
        task.Status is ProcessingStatus.Done or ProcessingStatus.Failed or ProcessingStatus.Cancelled;

    private bool IsUnfinished(ProcessingTask task) => !IsFinished(task);

    public async Task<CancellationTokenSource?> TryStartAsync(string uploadId, CancellationToken ct)
    {
        var cts = running.Register(uploadId, ct);

        // Cancelled between the first read and the registration?
        if ((await repo.GetTaskByUploadIdAsync(uploadId))?.Status == ProcessingStatus.Cancelled)
        {
            running.Unregister(uploadId, cts);
            return null;
        }

        await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.InProgress);
        return cts;
    }

    public Task CompleteAsync(string uploadId) => repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Done);

    public Task FailAsync(string uploadId, string message, string errorCode) =>
        repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Failed, message, errorCode);

    public void End(string uploadId, CancellationTokenSource cts) => running.Unregister(uploadId, cts);

    public async Task<ProcessingStatus> CancelAsync(string uploadId)
    {
        var task = await repo.GetTaskByUploadIdAsync(uploadId)
            ?? throw new CityFyException(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound);

        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Upload '{uploadId}' is {task.Status}", StatusCodes.Status409Conflict);

        // Status first (a queued upload will be skipped), then stop the running import if any
        await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Cancelled, "Cancelled by an administrator");
        running.Cancel(uploadId);
        return task.Status;
    }

    public async Task<bool> CancelIfActiveAsync(ProcessingTask task)
    {
        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            return false;

        try
        {
            await CancelAsync(task.UploadId);
            return true;
        }
        catch (CityFyException e) when (e.Code is ErrorCodes.ProcessNotCancellable or ErrorCodes.UploadNotFound)
        {
            return false; // finished meanwhile
        }
    }

    public Task<long> DeleteAllAsync(string userId, CancellationToken ct) => repo.DeleteTasksByUserAsync(userId, ct);

    public async Task<(long Moved, int StillImporting)> TransferAsync(string fromUserId, string toUserId, CancellationToken ct)
    {
        var moved = await repo.TransferFinishedTasksAsync(fromUserId, toUserId, ct);
        var importing = (await repo.GetTasksByUserAsync(fromUserId, ct)).Count(IsUnfinished);
        return (moved, importing);
    }

    public Task DropLegacyAsync(string uploadId, CancellationToken ct) => repo.DropLegacyUploadAsync(uploadId, ct);
}