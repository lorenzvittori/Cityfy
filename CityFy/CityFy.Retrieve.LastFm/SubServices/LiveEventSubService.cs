using CityFy.Retrieve.LastFm.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LiveEventSubService(IMessagePublisher publisher) : ILiveEventSubService
{
    public Task PublishNowPlayingAsync(string userId, NowPlaying? playing, CancellationToken ct) =>
        publisher.PublishAsync(
            new NowPlayingChanged(userId, playing?.Artist, playing?.Track, playing?.Album, playing?.ArtistMbid, playing?.SeenAt, DateTime.UtcNow),
            correlationId: userId, ct);
}
