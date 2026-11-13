using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

/// <summary>
/// Open live connections (api/v1/live) per user, in memory: a NowPlayingChanged goes to every open
/// page of that user. One gateway instance: with more, every instance needs its own queue (fan-out).
/// </summary>
public interface ILiveHubSubService
{
    /// <summary>Null when the user already has MaxConnectionsPerUser pages open.</summary>
    LiveSubscription? TrySubscribe(string userId);

    /// <summary>To every open page of the user (a slow page loses the older events, never blocks the others).</summary>
    void Publish(string userId, NowPlayingDto playing);
}
