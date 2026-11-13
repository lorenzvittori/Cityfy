using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>
/// The track each user is listening to, in memory only (lost on restart, refilled by the next poll).
/// The browser will receive it in phase 5 of PIANO_LASTFM.md (NowPlayingChanged).
/// </summary>
public interface INowPlayingStoreSubService
{
    /// <summary>Stores what the poll saw (null = nothing playing); true when it changed.</summary>
    bool Set(string userId, LastFmNowPlaying? playing, DateTime now);

    NowPlaying? Get(string userId);

    /// <summary>True when something was playing.</summary>
    bool Forget(string userId);

    void ForgetAll();
}
