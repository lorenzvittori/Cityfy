using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Services;

public interface IRetrieveJobQueue
{
    ValueTask EnqueueAsync(RetrieveJob job, CancellationToken ct);
    IAsyncEnumerable<RetrieveJob> ReadAllAsync(CancellationToken ct);
}
