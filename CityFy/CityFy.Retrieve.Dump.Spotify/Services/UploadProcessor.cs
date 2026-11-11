using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IUploadProcessor
{
    /// <summary>
    /// Imports an extracted upload and publishes ListeningImported. Throws on failure: the
    /// messaging layer retries, and marks the task FAILED after the last attempt.
    /// </summary>
    Task ProcessAsync(string uploadId, CancellationToken ct);

    /// <summary>FAILED with the error code of the exception (UPLOAD_PROCESSING_FAILED if not a CityFyException).</summary>
    Task MarkFailedAsync(string uploadId, Exception error);
}

public class UploadProcessor : IUploadProcessor
{
    private readonly IStreamingRepository _repo;
    private readonly IStreamingHistoryService _history;
    private readonly IFileClient _files;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<UploadProcessor> _logger;

    public UploadProcessor(IStreamingRepository repo, IStreamingHistoryService history, IFileClient files,
        IMessagePublisher publisher, ILogger<UploadProcessor> logger)
    {
        _repo = repo;
        _history = history;
        _files = files;
        _publisher = publisher;
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

        // Duplicate message (e.g. startup recovery + queued message): already finished
        if (task.Status is ProcessingStatus.DONE or ProcessingStatus.FAILED)
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

        _logger.LogInformation("Processing upload {UploadId} for user {UserId}", uploadId, task.UserId);
        await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.WORKING_IN_PROGRESS);

        // Idempotent (upsert): a retry after a partial import does not duplicate plays
        var inserted = await _history.ProcessExtractedFilesAsync(uploadId, task.UserId, extracted, ct);

        await _publisher.PublishAsync(
            new ListeningImported(task.UserId, ListeningSources.Dump, uploadId, inserted, DateTime.UtcNow),
            correlationId: uploadId, ct);

        await _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.DONE);
    }

    public Task MarkFailedAsync(string uploadId, Exception error) =>
        _repo.UpdateTaskStatusAsync(uploadId, ProcessingStatus.FAILED, error.Message,
            error is CityFyException cf ? cf.Code : ErrorCodes.UploadProcessingFailed);
}
