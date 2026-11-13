using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Services;

/// <summary>History imports waiting to run (in memory: the recovery at startup queues the unfinished ones again).</summary>
public interface ILastFmImportQueue
{
    ValueTask EnqueueAsync(LastFmImportJob job, CancellationToken ct);

    IAsyncEnumerable<LastFmImportJob> ReadAllAsync(CancellationToken ct);
}
