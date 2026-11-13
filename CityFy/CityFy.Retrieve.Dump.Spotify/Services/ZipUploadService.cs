using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.SubServices;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class ZipUploadService(
    IUploadValidationSubService validation,
    IUploadStorageSubService storage,
    IUploadTaskSubService tasks,
    IUploadEventsSubService events,
    ILogger<ZipUploadService> logger) : IZipUploadService
{
    public async Task<UploadResult> HandleZipUploadAsync(IFormFile file, string userId, CancellationToken ct)
    {
        var owner = validation.Validate(file, userId);
        var upload = await storage.SaveAndExtractAsync(file, ct);

        // Pending task + event for UploadReceivedConsumer. Errors are surfaced to the caller: if the
        // event cannot be published the task stays Pending and UploadRecoveryService re-queues it
        // at the next start (the import is idempotent).
        await tasks.CreateAsync(upload.Id, owner);
        await events.PublishReceivedAsync(upload.Id, owner, ct);

        logger.LogInformation("Upload {UploadId} of user {UserId} stored: {Files} files", upload.Id, owner, upload.Files.Length);
        return upload;
    }

    public UploadResult? GetUploadResult(string id) => storage.GetExtracted(id);
}