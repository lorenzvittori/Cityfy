using System.Collections.Concurrent;
using System.Threading.Channels;
using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

public sealed class LiveHubSubService : ILiveHubSubService
{
    /// <summary>Pages (tabs) open at the same time per user.</summary>
    public const int MaxConnectionsPerUser = 5;

    /// <summary>Events kept for a page that does not read: only the latest track matters.</summary>
    private const int BufferPerConnection = 4;

    private readonly ConcurrentDictionary<string, List<Channel<NowPlayingDto>>> _connections = new();

    public LiveSubscription? TrySubscribe(string userId)
    {
        var channel = Channel.CreateBounded<NowPlayingDto>(new BoundedChannelOptions(BufferPerConnection)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        var list = _connections.GetOrAdd(userId, _ => []);
        lock (list)
        {
            if (list.Count >= MaxConnectionsPerUser)
                return null;
            list.Add(channel);
        }
        return new LiveSubscription(channel.Reader, () =>
        {
            lock (list)
                list.Remove(channel);
            channel.Writer.TryComplete();
        });
    }

    public void Publish(string userId, NowPlayingDto playing)
    {
        if (!_connections.TryGetValue(userId, out var list))
            return;
        lock (list)
            foreach (var channel in list)
                channel.Writer.TryWrite(playing);
    }
}
