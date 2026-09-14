using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Models;
using Microsoft.Extensions.Options;
using System.IO.Compression;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public class ZipUploadService : IZipUploadService
    {
        private readonly IFileClient _fileClient;
        private readonly DebugUploadOptions _options;
        private readonly IStreamingBlPersistenceService? _persistenceService;
        private readonly Microsoft.Extensions.Logging.ILogger<ZipUploadService> _logger;

        public ZipUploadService(IFileClient fileClient, IOptions<DebugUploadOptions> options, IStreamingBlPersistenceService? persistenceService = null, Microsoft.Extensions.Logging.ILogger<ZipUploadService>? logger = null)
        {
            _fileClient = fileClient;
            _options = options.Value;
            _persistenceService = persistenceService;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ZipUploadService>.Instance;
        }

        public async Task<UploadResult> HandleZipUploadAsync(IFormFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            if (file.Length > _options.MaxUploadSizeBytes)
                throw new InvalidOperationException("File too large");

            if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only .zip files are accepted");

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

            // Extract using temporary file
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir);
            }
            catch (InvalidDataException)
            {
                throw;
            }

            var files = _fileClient.ListFiles(extractDir);

            // create a processing task in mongo so hosted service will pick it up
            try
            {
                if (_persistenceService != null)
                {
                    await _persistenceService.CreateTaskAsync(id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create processing task: {Message}", ex.Message);
            }

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
