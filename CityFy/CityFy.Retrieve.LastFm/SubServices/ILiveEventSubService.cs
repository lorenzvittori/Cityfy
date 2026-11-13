using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

public interface ILiveEventSubService
{
    /// <summary>NowPlayingChanged for the open pages of the user (null = nothing playing any more).</summary>
    Task PublishNowPlayingAsync(string userId, NowPlaying? playing, CancellationToken ct);
}
