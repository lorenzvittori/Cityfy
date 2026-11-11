using System.Threading.Channels;
using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Services
{
    public interface IRetrieveJobQueue
    {
        ValueTask EnqueueAsync(RetrieveJob job, CancellationToken ct);
        IAsyncEnumerable<RetrieveJob> ReadAllAsync(CancellationToken ct);
    }

    // In-memory queue: jobs still queued are lost on restart and their task stays Pending.
    public class RetrieveJobQueue : IRetrieveJobQueue
    {
        private readonly Channel<RetrieveJob> _channel = Channel.CreateBounded<RetrieveJob>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });

        public ValueTask EnqueueAsync(RetrieveJob job, CancellationToken ct) => _channel.Writer.WriteAsync(job, ct);

        public IAsyncEnumerable<RetrieveJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }
}
