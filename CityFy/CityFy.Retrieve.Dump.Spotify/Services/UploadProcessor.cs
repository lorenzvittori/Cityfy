using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class UploadProcessor(
    IStreamingRepository repo,
    IStreamingHistoryService history,
    IFileClient files,
    IMessagePublisher publisher,
    CancellationRegistry running,
    ILogger<UploadProcessor> logger) : IUploadProcessor
{
    private readonly IFileClient _files = files;
    private readonly CancellationRegistry _running = running;

    public async Task ProcessAsync(string uploadId, CancellationToken ct)
    {
        var task = await repo.GetTaskByUploadIdAsync(uploadId);
        if (task == null)
        {
            logger.LogWarning("Upload {UploadId}: no task, nothing to do", uploadId);
            return;
        }

        // Duplicate message (e.g. startup recovery + queued message) or cancelled while queued
        if (task.Status is ProcessingStatus.Done or ProcessingStatus.Failed or ProcessingStatus.Cancelled)
        {
            logger.LogInformation("Upload {UploadId} already {Status}, skipped", uploadId, task.Status);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.UserId))
        {
            await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Failed,
                "Upload without userId: re-upload with the userId form field", ErrorCodes.UploadUserMissing);
            return;
        }

        var extracted = Path.Combine(_files.GetFullPath("uploads", uploadId), "extracted");
        if (!Directory.Exists(extracted))
        {
            await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Failed,
                "Extracted files not found", ErrorCodes.UploadProcessingFailed);
            return;
        }

        var cts = _running.Register(uploadId, ct);
        try
        {
            // Cancelled between the first read and the registration?
            if ((await repo.GetTaskByUploadIdAsync(uploadId))?.Status == ProcessingStatus.Cancelled)
                return;

            logger.LogInformation("Processing upload {UploadId} for user {UserId}", uploadId, task.UserId);
            await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.InProgress);

            // Idempotent (upsert): a retry after a partial import does not duplicate plays
            var inserted = await history.ProcessExtractedFilesAsync(uploadId, task.UserId, extracted, cts.Token);

            await publisher.PublishAsync(
                new ListeningImported(task.UserId, ListeningSources.Dump, uploadId, inserted, DateTime.UtcNow),
                correlationId: uploadId, cts.Token);

            await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Done);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Stopped by the admin (status already Cancelled): plays imported so far stay (idempotent)
            logger.LogInformation("Upload {UploadId} cancelled by the admin", uploadId);
        }
        finally
        {
            _running.Unregister(uploadId, cts);
        }
    }

    public Task MarkFailedAsync(string uploadId, Exception error) =>
        repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Failed, error.Message,
            error is CityFyException cf ? cf.Code : ErrorCodes.UploadProcessingFailed);

    public async Task CancelAsync(string uploadId)
    {
        var task = await repo.GetTaskByUploadIdAsync(uploadId)
            ?? throw new CityFyException(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound);

        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.InProgress))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Upload '{uploadId}' is {task.Status}", StatusCodes.Status409Conflict);

        // Status first (a queued upload will be skipped), then stop the running import if any
        await repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.Cancelled, "Cancelled by an administrator");
        _running.Cancel(uploadId);
        logger.LogInformation("Upload {UploadId} cancelled ({Status})", uploadId, task.Status);
    }
}
