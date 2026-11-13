using System.Net.ServerSentEvents;
using CityFy.Api.Models;
using ServiceDefault.Contracts;

namespace CityFy.Api.Services;

/// <summary>Live events of a user's pages (api/v1/live, Server-Sent Events).</summary>
public interface ILiveService
{
    /// <summary>
    /// Opens a live connection of an account, with the track playing now. Throws RATE_LIMITED (429)
    /// when the user already has too many pages open, LASTFM_NOT_LINKED (404), LASTFM_UNAVAILABLE.
    /// </summary>
    Task<LiveConnection> OpenAsync(string userId, CancellationToken ct);

    /// <summary>
    /// The events of the connection until <paramref name="ct"/> (the page closes): first the track
    /// playing now, then every change ("now-playing"), a "ping" every 25 s against idle-connection
    /// timeouts, and a UserActive every minute towards Retrieve.LastFm. Disposes the subscription.
    /// </summary>
    IAsyncEnumerable<SseItem<NowPlayingDto?>> ReadAsync(string userId, LiveConnection connection, CancellationToken ct);

    /// <summary>A NowPlayingChanged from Retrieve.LastFm to the open pages of the user (dropped when older than a few minutes).</summary>
    Task OnNowPlayingChangedAsync(NowPlayingChanged message, CancellationToken ct);
}
