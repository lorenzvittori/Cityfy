using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class LegacyUploadMigrationSubService(IStreamingRepository repo, IListeningEventStore store, IStreamingMapper mapper)
    : ILegacyUploadMigrationSubService
{
    private const int BatchSize = 1000;

    public async Task<int> MigrateAsync(string uploadId, string userId, CancellationToken ct)
    {
        var total = 0;
        await foreach (var batch in repo.ReadLegacyUploadAsync(uploadId, BatchSize, ct))
        {
            var events = batch.Select(b => mapper.MapToListeningEvent(b, userId, uploadId)).OfType<ListeningEvent>().ToList();
            total += await store.UpsertAsync(events, ListeningSources.Dump, ct);
        }
        return total;
    }
}