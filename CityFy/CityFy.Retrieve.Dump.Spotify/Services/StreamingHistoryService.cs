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

        public StreamingHistoryService(IStreamingBlPersistenceService persistence, MongoOptions mongoOptions, IStreamingMapper mapper)
        {
            _persistence = persistence;
            _mongoOptions = mongoOptions;
            _mapper = mapper;
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
                Console.WriteLine("No JSON files found in extracted folder: " + extractDir);
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
                            Console.WriteLine("Skipping JSON file (no array/dtos): " + file);
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
                                Console.WriteLine("Failed to map element from " + file + " : " + e.Message);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to read/parse file " + file + " : " + e.Message);
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
                    Console.WriteLine($"Inserted {totalInserted} documents into collection {collectionName}");
                }
                else
                {
                    Console.WriteLine("No documents to insert for upload " + uploadId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while processing extracted files: " + ex);
            }
        }
    }
}
