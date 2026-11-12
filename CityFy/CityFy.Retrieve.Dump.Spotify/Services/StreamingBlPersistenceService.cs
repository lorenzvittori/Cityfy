using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Listening;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class StreamingBlPersistenceService(
    IStreamingRepository repository,
    IListeningEventStore store,
    IStreamingMapper mapper,
    ILogger<StreamingBlPersistenceService> logger) : IStreamingBlPersistenceService
{
    private const int MigrationBatchSize = 1000;

    public async Task<int> PersistBufferAsync(string userId, IReadOnlyList<StreamingBl> buffer, string uploadId, CancellationToken ct = default)
    {
        if (buffer.Count == 0) return 0;

        var events = buffer
            .Select(b => mapper.MapToListeningEvent(b, userId, uploadId))
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        // Errors propagate: the caller marks the task Failed instead of reporting a partial import as Done
        var inserted = await store.UpsertAsync(events, ListeningSources.Dump, ct);

        if (inserted > 0)
        {
            try
            {
                await repository.IncrementTaskInsertedAsync(uploadId, inserted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update task progress for {UploadId}", uploadId);
            }
        }

        return inserted;
    }

    public Task CreateTaskAsync(string uploadId, string userId) =>
        repository.CreateTaskAsync(new ProcessingTask
        {
            UploadId = uploadId,
            UserId = userId,
            Status = ProcessingStatus.Pending,
            Inserted = 0,
            StartedAt = DateTime.UtcNow
        });

    public async Task ReportProgressAsync(string uploadId, double? percent, string? detail)
    {
        try
        {
            await repository.UpdateTaskProgressAsync(uploadId, percent, detail);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Progress update failed for {UploadId}", uploadId);
        }
    }

    public Task UpdateTaskStatusAsync(string uploadId, ProcessingStatus status, string? message = null) =>
        repository.UpdateTaskStatusAsync(uploadId, status, message);

    public Task<IEnumerable<ProcessingTask>> GetTasksByStatusAsync(ProcessingStatus status) =>
        repository.GetTasksByStatusAsync(status);

    public async Task<int> MigrateLegacyUploadAsync(string uploadId, string userId, CancellationToken ct = default)
    {
        var total = 0;
        await foreach (var batch in repository.ReadLegacyUploadAsync(uploadId, MigrationBatchSize, ct))
        {
            var events = batch
                .Select(b => mapper.MapToListeningEvent(b, userId, uploadId))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();
            total += await store.UpsertAsync(events, ListeningSources.Dump, ct);
        }

        logger.LogInformation("Legacy upload {UploadId} migrated for user {UserId}: {Inserted} new plays", uploadId, userId, total);
        return total;
    }
}
