using System.Text.Json;
using CityFy.Retrieve.Dump.Spotify.JsonConverters;
using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public class StreamingHistoryService : IStreamingHistoryService
    {
        private const int BatchSize = 1000;

        // Extended streaming history files: Streaming_History_Audio_*.json / Streaming_History_Video_*.json.
        // Other files of an account export (Userdata.json, Playlist*.json, ...) have a different schema.
        private const string StreamingFilePrefix = "Streaming_History_";

        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private readonly IStreamingBlPersistenceService _persistence;
        private readonly IStreamingMapper _mapper;
        private readonly ILogger<StreamingHistoryService> _logger;

        public StreamingHistoryService(IStreamingBlPersistenceService persistence, IStreamingMapper mapper, ILogger<StreamingHistoryService> logger)
        {
            _persistence = persistence;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<int> ProcessExtractedFilesAsync(string uploadId, string userId, string extractDir, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(uploadId)) throw new ArgumentNullException(nameof(uploadId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrWhiteSpace(extractDir)) throw new ArgumentNullException(nameof(extractDir));
            if (!Directory.Exists(extractDir)) throw new DirectoryNotFoundException(extractDir);

            var jsonFiles = Directory.EnumerateFiles(extractDir, "*.json", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f).StartsWith(StreamingFilePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .ToArray();

            if (jsonFiles.Length == 0)
                throw new CityFyException(ErrorCodes.UploadNoStreamingFiles, $"No '{StreamingFilePrefix}*.json' files found in the upload");

            var buffer = new List<StreamingBl>(BatchSize);
            var totalInserted = 0;
            var failedFiles = 0;

            foreach (var file in jsonFiles)
            {
                ct.ThrowIfCancellationRequested();

                List<StreamingDto>? dtos;
                try
                {
                    await using var stream = File.OpenRead(file);
                    dtos = await JsonSerializer.DeserializeAsync<List<StreamingDto>>(stream, JsonOptions, ct);
                }
                catch (JsonException ex)
                {
                    // A malformed file is skipped; the others are still imported
                    failedFiles++;
                    _logger.LogError(ex, "Failed to parse {File}", file);
                    continue;
                }

                if (dtos == null) continue;

                foreach (var bl in _mapper.MapToBl(dtos))
                {
                    buffer.Add(bl);
                    if (buffer.Count >= BatchSize)
                    {
                        totalInserted += await _persistence.PersistBufferAsync(userId, buffer, uploadId, ct);
                        buffer.Clear();
                    }
                }
            }

            if (buffer.Count > 0)
                totalInserted += await _persistence.PersistBufferAsync(userId, buffer, uploadId, ct);

            if (failedFiles == jsonFiles.Length)
                throw new CityFyException(ErrorCodes.UploadUnreadableFiles, "None of the streaming history files could be parsed");

            _logger.LogInformation("Upload {UploadId}: {Files} files ({Failed} unreadable), {Inserted} new plays for user {UserId}",
                uploadId, jsonFiles.Length, failedFiles, totalInserted, userId);

            return totalInserted;
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new NullableDateTimeJsonConverter());
            return options;
        }
    }
}
