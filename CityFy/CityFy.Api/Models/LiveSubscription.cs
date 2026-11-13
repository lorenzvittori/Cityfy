using System.Threading.Channels;

namespace CityFy.Api.Models;

/// <summary>One open page of api/v1/live: its events, until disposed.</summary>
public sealed class LiveSubscription(ChannelReader<NowPlayingDto> events, Action unsubscribe) : IDisposable
{
    private int _disposed;

    public ChannelReader<NowPlayingDto> Events { get; } = events;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            unsubscribe();
    }
}

/// <summary>An open live connection: its subscription and the track playing when it opened.</summary>
public sealed record LiveConnection(LiveSubscription Subscription, NowPlayingDto Initial);
