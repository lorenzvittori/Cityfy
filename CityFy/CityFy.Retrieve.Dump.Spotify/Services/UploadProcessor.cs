using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IUploadProcessor
{
    /// <summary>
    /// Imports an extracted upload and publishes ListeningImported. Throws on failure: the
    /// messaging layer retries, and marks the task FAILED after the last attempt.
    /// A cancellation by the admin ends normally with the task CANCELLED (no retry).
    /// </summary>
    Task ProcessAsync(string uploadId, CancellationToken ct);

    /// <summary>FAILED with the error code of the exception (UPLOAD_PROCESSING_FAILED if not a CityFyException).</summary>
    Task MarkFailedAsync(string uploadId, Exception error);

    /// <summary>
    /// Admin: Pending -> CANCELLED (never starts); WORKING -> CANCELLED and the running import stops.
    /// Throws UPLOAD_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409) when already finished.
    /// </summary>
    Task CancelAsync(string uploadId);
}

public class UploadProcessor : IUploadProcessor
{
    private readonly IStreamingRepository _repo;
    private readonly IStreamingHistoryService _history;
    private readonly IFileClient _files;
    private readonly IMessagePublisher _publisher;
    private readonly CancellationRegistry _running;
    private readonly ILogger<UploadProcessor> _logger;

    public UploadProcessor(IStreamingRepository repo, IStreamingHistoryService history, IFileClient files,
        IMessagePublisher publisher, CancellationRegistry running, ILogger<UploadProcessor> logger)
    {
        _repo = repo;
        _history = history;
        _files = files;
        _publisher = publisher;
        _running = running;
        _logger = logger;
    }

    public async Task ProcessAsync(string uploadId, CancellationToken ct)
    {
        var task = await _repo.GetTaskByUploadIdAsync(uploadId);
        if (task == null)
        {
            _logger.LogWarning("Upload {UploadId}: no task, nothing to do", uploadId);
            return;
        }

        // Duplicate message (e.g. startup recovery + queued message) or cancelled while queued
        if (task.Status is ProcessingStatus.DONE or ProcessingStatus.FAILED or ProcessingStatus.CANCELLED)
        {
            _logger.LogInformation("Upload {UploadId} already {Status}, skipped", uploadId, task.Status);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.UserId))
        {
            await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.FAILED,
                "Upload without userId: re-upload with the userId form field", ErrorCodes.UploadUserMissing);
            return;
        }

        var extracted = Path.Combine(_files.GetFullPath("uploads", uploadId), "extracted");
        if (!Directory.Exists(extracted))
        {
            await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.FAILED,
                "Extracted files not found", ErrorCodes.UploadProcessingFailed);
            return;
        }

        var cts = _running.Register(uploadId, ct);
        try
        {
            // Cancelled between the first read and the registration?
            if ((await _repo.GetTaskByUploadIdAsync(uploadId))?.Status == ProcessingStatus.CANCELLED)
                return;

            _logger.LogInformation("Processing upload {UploadId} for user {UserId}", uploadId, task.UserId);
            await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.WORKING_IN_PROGRESS);

            // Idempotent (upsert): a retry after a partial import does not duplicate plays
            var inserted = await _history.ProcessExtractedFilesAsync(uploadId, task.UserId, extracted, cts.Token);

            await _publisher.PublishAsync(
                new ListeningImported(task.UserId, ListeningSources.Dump, uploadId, inserted, DateTime.UtcNow),
                correlationId: uploadId, cts.Token);

            await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.DONE);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Stopped by the admin (status already CANCELLED): plays imported so far stay (idempotent)
            _logger.LogInformation("Upload {UploadId} cancelled by the admin", uploadId);
        }
        finally
        {
            _running.Unregister(uploadId, cts);
        }
    }

    public Task MarkFailedAsync(string uploadId, Exception error) =>
        _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.FAILED, error.Message,
            error is CityFyException cf ? cf.Code : ErrorCodes.UploadProcessingFailed);

    public async Task CancelAsync(string uploadId)
    {
        var task = await _repo.GetTaskByUploadIdAsync(uploadId)
            ?? throw new CityFyException(ErrorCodes.UploadNotFound, $"Upload '{uploadId}' not found", StatusCodes.Status404NotFound);

        if (task.Status is not (ProcessingStatus.Pending or ProcessingStatus.WORKING_IN_PROGRESS))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"Upload '{uploadId}' is {task.Status}", StatusCodes.Status409Conflict);

        // Status first (a queued upload will be skipped), then stop the running import if any
        await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.CANCELLED, "Annullato dall'amministratore");
        _running.Cancel(uploadId);
        _logger.LogInformation("Upload {UploadId} cancelled ({Status})", uploadId, task.Status);
    }
}
