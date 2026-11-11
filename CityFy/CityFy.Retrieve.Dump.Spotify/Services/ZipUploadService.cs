using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Models;
using Microsoft.Extensions.Options;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Messaging;
using System.IO.Compression;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public class ZipUploadService : IZipUploadService
    {
        private readonly IFileClient _fileClient;
        private readonly DebugUploadOptions _options;
        private readonly IStreamingBlPersistenceService _persistenceService;
        private readonly IMessagePublisher _publisher;

        public ZipUploadService(IFileClient fileClient, IOptions<DebugUploadOptions> options,
            IStreamingBlPersistenceService persistenceService, IMessagePublisher publisher)
        {
            _fileClient = fileClient;
            _options = options.Value;
            _persistenceService = persistenceService;
            _publisher = publisher;
        }

        public async Task<UploadResult> HandleZipUploadAsync(IFormFile file, string userId)
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
            var uploadDir = _fileClient.GetFullPath("uploads", id);
            _fileClient.CreateDirectory(uploadDir);

            var zipPath = Path.Combine(uploadDir, Path.GetFileName(file.FileName));
            await using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);
                await _fileClient.SaveFileAsync(zipPath, ms);
            }

            var extractDir = Path.Combine(uploadDir, "extracted");
            _fileClient.CreateDirectory(extractDir);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir);
            }
            catch (InvalidDataException ex)
            {
                throw new CityFyException(ErrorCodes.UploadInvalidZip, "The file is not a valid zip archive", inner: ex);
            }

            var files = _fileClient.ListFiles(extractDir);

            // Pending task + event for UploadReceivedConsumer. Errors are surfaced to the caller: if the
            // event cannot be published the task stays Pending and UploadRecoveryService re-queues it
            // at the next start (the import is idempotent).
            await _persistenceService.CreateTaskAsync(id, userId.Trim());
            await _publisher.PublishAsync(new UploadReceived(id, userId.Trim()), correlationId: id);

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

            var extractDir = _fileClient.GetFullPath("uploads", id, "extracted");
            if (!Directory.Exists(extractDir)) return null;

            var files = _fileClient.ListFiles(extractDir);
            return new UploadResult
            {
                Id = id,
                UploadPath = _fileClient.GetFullPath("uploads", id),
                ExtractedPath = extractDir,
                Files = files
            };
        }
    }
}
