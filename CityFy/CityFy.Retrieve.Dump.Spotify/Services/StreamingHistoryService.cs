using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using ServiceDefault.Models;
using System.Text.Json;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public class StreamingHistoryService : IStreamingHistoryService
    {
        private readonly IStreamingBlPersistenceService _persistence;
        private readonly MongoOptions _mongoOptions;
        private readonly IStreamingMapper _mapper;
        private readonly Microsoft.Extensions.Logging.ILogger<StreamingHistoryService> _logger;

        public StreamingHistoryService(IStreamingBlPersistenceService persistence, MongoOptions mongoOptions, IStreamingMapper mapper, Microsoft.Extensions.Logging.ILogger<StreamingHistoryService> logger)
        {
            _persistence = persistence;
            _mongoOptions = mongoOptions;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task ProcessExtractedFilesAsync(string uploadId, string extractDir)
        {
            if (string.IsNullOrWhiteSpace(uploadId)) throw new ArgumentNullException(nameof(uploadId));
            if (string.IsNullOrWhiteSpace(extractDir)) throw new ArgumentNullException(nameof(extractDir));
            if (!Directory.Exists(extractDir)) return;

            var jsonFiles = Directory.EnumerateFiles(extractDir, "*.json", SearchOption.AllDirectories)
                .OrderBy(n => n)
                .ToArray();

            if (!jsonFiles.Any())
            {
                _logger.LogWarning("No JSON files found in extracted folder: {ExtractDir}", extractDir);
                return;
            }

            var collectionName = (_mongoOptions.CollectionPrefix ?? "streaming_history_") + uploadId;
            try
            {
                await _persistence.EnsureCollectionExistsAsync(collectionName);

                // Process files and save in batches while reading to avoid storing all data in memory
                const int batchSize = 1000;
                var buffer = new List<StreamingBl>(batchSize);
                var totalInserted = 0;

                foreach (var file in jsonFiles)
                {
                    try
                    {
                        var txt = await File.ReadAllTextAsync(file);
                        var options = new System.Text.Json.JsonSerializerOptions();
                        options.Converters.Add(new CityFy.Retrieve.Dump.Spotify.JsonConverters.NullableDateTimeJsonConverter());
                        var dtos = JsonSerializer.Deserialize<IEnumerable<StreamingDto>>(txt, options);
                        if (dtos == null)
                        {
                            _logger.LogWarning("Skipping JSON file (no array/dtos): {File}", file);
                            continue;
                        }

                        var mappedBls = _mapper.MapToBl(dtos);
                        foreach (var bl in mappedBls)
                        {
                            try
                            {
                                buffer.Add(bl);
                                if (buffer.Count >= batchSize)
                                {
                                    var inserted = await _persistence.PersistBufferAsync(collectionName, buffer, uploadId);
                                    totalInserted += inserted;
                                    buffer.Clear();
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, "Failed to map element from {File} : {Message}", file, e.Message);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to read/parse file {File} : {Message}", file, e.Message);
                    }
                }

                // flush remaining buffer
                if (buffer.Count > 0)
                {
                    var inserted = await _persistence.PersistBufferAsync(collectionName, buffer, uploadId);
                    totalInserted += inserted;
                    buffer.Clear();
                }

                if (totalInserted > 0)
                {
                    _logger.LogInformation("Inserted {TotalInserted} documents into collection {Collection}", totalInserted, collectionName);
                }
                else
                {
                    _logger.LogInformation("No documents to insert for upload {UploadId}", uploadId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing extracted files: {Message}", ex.Message);
            }
        }
    }
}
