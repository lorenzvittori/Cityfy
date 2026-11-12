using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Messaging;
using System.IO.Compression;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class ZipUploadService(
    IFileClient fileClient,
    IOptions<UploadOptions> options,
    IStreamingBlPersistenceService persistenceService,
    IMessagePublisher publisher) : IZipUploadService
{
    private readonly UploadOptions _options = options.Value;

    public async Task<UploadResult> HandleZipUploadAsync(IFormFile file, string userId, CancellationToken ct)
    {
        if (file == null)
            throw new CityFyException(ErrorCodes.UploadFileMissing, "Missing file form field (use 'file')");
        if (string.IsNullOrWhiteSpace(userId))
            throw new CityFyException(ErrorCodes.UploadUserMissing, "Missing userId form field (owner of the export)");

        if (file.Length > _options.MaxUploadSizeBytes)
            throw new CityFyException(ErrorCodes.UploadTooLarge,
                $"File too large: {file.Length} bytes, limit {_options.MaxUploadSizeBytes}", StatusCodes.Status413PayloadTooLarge);

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new CityFyException(ErrorCodes.UploadNotZip, "Only .zip files are accepted");

        var id = Guid.NewGuid().ToString("N");
        var uploadDir = fileClient.GetFullPath("uploads", id);
        fileClient.CreateDirectory(uploadDir);

        var zipPath = Path.Combine(uploadDir, Path.GetFileName(file.FileName));
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            ms.Seek(0, SeekOrigin.Begin);
            await fileClient.SaveFileAsync(zipPath, ms);
        }

        var extractDir = Path.Combine(uploadDir, "extracted");
        fileClient.CreateDirectory(extractDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
        }
        catch (InvalidDataException ex)
        {
            throw new CityFyException(ErrorCodes.UploadInvalidZip, "The file is not a valid zip archive", inner: ex);
        }

        var files = fileClient.ListFiles(extractDir);

        // Pending task + event for UploadReceivedConsumer. Errors are surfaced to the caller: if the
        // event cannot be published the task stays Pending and UploadRecoveryService re-queues it
        // at the next start (the import is idempotent).
        await persistenceService.CreateTaskAsync(id, userId.Trim());
        await publisher.PublishAsync(new UploadReceived(id, userId.Trim()), correlationId: id, ct);

        return new UploadResult
        {
            Id = id,
            UploadPath = uploadDir,
            ExtractedPath = extractDir,
            Files = files
        };
    }

    public UploadResult? GetUploadResult(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var extractDir = fileClient.GetFullPath("uploads", id, "extracted");
        if (!Directory.Exists(extractDir)) return null;

        var files = fileClient.ListFiles(extractDir);
        return new UploadResult
        {
            Id = id,
            UploadPath = fileClient.GetFullPath("uploads", id),
            ExtractedPath = extractDir,
            Files = files
        };
    }
}
