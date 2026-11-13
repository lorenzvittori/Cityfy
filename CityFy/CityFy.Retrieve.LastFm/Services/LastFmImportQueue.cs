using System.Threading.Channels;
using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Services;

public sealed class LastFmImportQueue : ILastFmImportQueue
{
    private const int Capacity = 100;

    private readonly Channel<LastFmImportJob> _channel = Channel.CreateBounded<LastFmImportJob>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ValueTask EnqueueAsync(LastFmImportJob job, CancellationToken ct) => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<LastFmImportJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
