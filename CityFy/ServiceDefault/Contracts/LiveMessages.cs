namespace ServiceDefault.Contracts;

// Live messages: what the user is listening to right now, towards the open pages.
//
//   Retrieve.LastFm  polling sees a different track (or none) ──► NowPlayingChanged
//   Api              NowPlayingChanged ──► SSE api/v1/live of the user's open pages
//   Api              a page open on api/v1/live, every minute ──► UserActive
//   Retrieve.LastFm  UserActive ──► the user is read at the fast cadence while the page stays open
//
// They describe a moment: a consumer drops one older than a few minutes instead of replaying it.

/// <summary>
/// The track playing for a user changed. Artist/Track null: nothing is playing any more.
/// StartedAt: when the polling first saw this track.
/// </summary>
public sealed record NowPlayingChanged(
    string UserId,
    string? Artist,
    string? Track,
    string? Album,
    string? ArtistMbid,
    DateTime? StartedAt,
    DateTime ChangedAt);

/// <summary>The user has a CityFy page open (live connection): read their plays at the fast cadence.</summary>
public sealed record UserActive(string UserId, DateTime At);
