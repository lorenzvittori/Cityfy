using CityFy.Retrieve.Dump.Spotify.SubServices;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class UploadProcessor(
    IUploadTaskSubService tasks,
    IUploadStorageSubService storage,
    IStreamingHistoryImportSubService history,
    ILegacyUploadMigrationSubService legacy,
    IUploadEventsSubService events,
    IUploadErrorSubService errors,
    IUploadValidationSubService validation,
    ILogger<UploadProcessor> logger) : IUploadProcessor
{
    public async Task ProcessAsync(string uploadId, CancellationToken ct)
    {
        var task = await tasks.GetAsync(uploadId);
        if (task == null)
        {
            logger.LogWarning("Upload {UploadId}: no task, nothing to do", uploadId);
            return;
        }

        // Duplicate message (e.g. startup recovery + queued message) or cancelled while queued
        if (tasks.IsFinished(task))
        {
            logger.LogInformation("Upload {UploadId} already {Status}, skipped", uploadId, task.Status);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.UserId))
        {
            await tasks.FailAsync(uploadId, "Upload without userId: re-upload with the userId form field", ErrorCodes.UploadUserMissing);
            return;
        }

        if (storage.GetExtractedDirectory(uploadId) is not { } extracted)
        {
            await tasks.FailAsync(uploadId, "Extracted files not found", ErrorCodes.UploadProcessingFailed);
            return;
        }

        if (await tasks.TryStartAsync(uploadId, ct) is not { } cts)
            return;

        try
        {
            logger.LogInformation("Processing upload {UploadId} for user {UserId}", uploadId, task.UserId);

            // Idempotent (upsert): a retry after a partial import does not duplicate plays
            var inserted = await history.ImportAsync(uploadId, task.UserId, extracted, cts.Token);
            await events.PublishImportedAsync(uploadId, task.UserId, inserted, cts.Token);
            await tasks.CompleteAsync(uploadId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Stopped by the admin (status already Cancelled): plays imported so far stay (idempotent)
            logger.LogInformation("Upload {UploadId} cancelled by the admin", uploadId);
        }
        finally
        {
            tasks.End(uploadId, cts);
        }
    }

    public Task MarkFailedAsync(string uploadId, Exception error) =>
        tasks.FailAsync(uploadId, error.Message, errors.ToErrorCode(error));

    public async Task CancelAsync(string uploadId)
    {
        var previous = await tasks.CancelAsync(uploadId);
        logger.LogInformation("Upload {UploadId} cancelled ({Status})", uploadId, previous);
    }

    public async Task<int> RequeueUnfinishedAsync(CancellationToken ct)
    {
        var unfinished = await tasks.ListUnfinishedAsync();
        foreach (var task in unfinished)
            await events.PublishReceivedAsync(task.UploadId, task.UserId!, ct);

        if (unfinished.Count > 0)
            logger.LogInformation("Re-queued {Count} unfinished uploads", unfinished.Count);
        return unfinished.Count;
    }

    public async Task<int> MigrateLegacyUploadAsync(string uploadId, string userId, CancellationToken ct)
    {
        var owner = validation.ValidateOwner(userId);
        var inserted = await legacy.MigrateAsync(uploadId, owner, ct);
        logger.LogInformation("Legacy upload {UploadId} migrated for user {UserId}: {Inserted} new plays", uploadId, owner, inserted);
        return inserted;
    }
}