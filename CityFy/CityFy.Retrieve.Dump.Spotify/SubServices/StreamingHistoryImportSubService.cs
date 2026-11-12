using System.Text.Json;
using CityFy.Retrieve.Dump.Spotify.JsonConverters;
using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Errors;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class StreamingHistoryImportSubService(
    IStreamingRepository repo,
    IListeningEventStore store,
    IStreamingMapper mapper,
    ILogger<StreamingHistoryImportSubService> logger) : IStreamingHistoryImportSubService
{
    private const int BatchSize = 1000;

    // Extended streaming history files: Streaming_History_Audio_*.json / Streaming_History_Video_*.json.
    // Other files of an account export (Userdata.json, Playlist*.json, ...) have a different schema.
    private const string StreamingFilePrefix = "Streaming_History_";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new NullableDateTimeJsonConverter() } };

    public async Task<int> ImportAsync(string uploadId, string userId, string extractDir, CancellationToken ct)
    {
        var jsonFiles = Directory.EnumerateFiles(extractDir, "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).StartsWith(StreamingFilePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n)
            .ToArray();

        if (jsonFiles.Length == 0)
            throw new CityFyException(ErrorCodes.UploadNoStreamingFiles, $"No '{StreamingFilePrefix}*.json' files found in the upload");

        var buffer = new List<StreamingBl>(BatchSize);
        var totalInserted = 0;
        var failedFiles = 0;

        // Progress by bytes: files of different years have very different sizes
        var totalBytes = Math.Max(1, jsonFiles.Sum(f => new FileInfo(f).Length));
        long doneBytes = 0;

        for (var index = 0; index < jsonFiles.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var file = jsonFiles[index];

            await ReportProgressAsync(uploadId, 100.0 * doneBytes / totalBytes,
                $"{Path.GetFileName(file)} ({index + 1}/{jsonFiles.Length}) · {totalInserted} new plays");
            doneBytes += new FileInfo(file).Length;

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
                logger.LogError(ex, "Failed to parse {File}", file);
                continue;
            }

            if (dtos == null) continue;

            foreach (var bl in mapper.MapToBl(dtos))
            {
                buffer.Add(bl);
                if (buffer.Count >= BatchSize)
                {
                    totalInserted += await PersistAsync(userId, buffer, uploadId, ct);
                    buffer.Clear();
                }
            }
        }

        if (buffer.Count > 0)
            totalInserted += await PersistAsync(userId, buffer, uploadId, ct);

        if (failedFiles == jsonFiles.Length)
            throw new CityFyException(ErrorCodes.UploadUnreadableFiles, "None of the streaming history files could be parsed");

        logger.LogInformation("Upload {UploadId}: {Files} files ({Failed} unreadable), {Inserted} new plays for user {UserId}",
            uploadId, jsonFiles.Length, failedFiles, totalInserted, userId);
        return totalInserted;
    }

    /// <summary>Maps a batch to listening events, upserts them and adds the NEW plays to the task counter.</summary>
    private async Task<int> PersistAsync(string userId, IReadOnlyList<StreamingBl> buffer, string uploadId, CancellationToken ct)
    {
        var events = buffer.Select(b => mapper.MapToListeningEvent(b, userId, uploadId)).OfType<ListeningEvent>().ToList();

        // Errors propagate: the caller marks the task Failed instead of reporting a partial import as Done
        var inserted = await store.UpsertAsync(events, ListeningSources.Dump, ct);
        if (inserted > 0)
        {
            try
            {
                await repo.IncrementTaskInsertedAsync(uploadId, inserted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update task progress for {UploadId}", uploadId);
            }
        }
        return inserted;
    }

    /// <summary>Progress shown in the admin panel: never fails the import.</summary>
    private async Task ReportProgressAsync(string uploadId, double? percent, string? detail)
    {
        try
        {
            await repo.UpdateTaskProgressAsync(uploadId, percent, detail);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Progress update failed for {UploadId}", uploadId);
        }
    }
}