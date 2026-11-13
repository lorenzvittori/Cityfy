namespace CityFy.Retrieve.LastFm.Models;

// What CityFy reads from the Last.fm API, already parsed (Mappers/LastFmResponseMapper).

/// <summary>auth.getSession: the verified user name and the session key (never expires unless revoked).</summary>
public sealed record LastFmSession(string Username, string SessionKey);

/// <summary>A play recorded on Last.fm. ScrobbledAt is when the play started (UTC).</summary>
public sealed record LastFmScrobble(
    string Artist,
    string Track,
    string? Album,
    string? ArtistMbid,
    string? TrackMbid,
    DateTime ScrobbledAt)
{
    public bool IsAfter(DateTime cursor) => ScrobbledAt > cursor;

    /// <summary>Strictly after <paramref name="cursor"/> and strictly before <paramref name="to"/>.</summary>
    public bool IsBetween(DateTime cursor, DateTime to) => ScrobbledAt > cursor && ScrobbledAt < to;

    public static DateTime ScrobbledAtOf(LastFmScrobble scrobble) => scrobble.ScrobbledAt;
}

/// <summary>The track the user is listening to right now (no date: it is not a scrobble yet).</summary>
public sealed record LastFmNowPlaying(string Artist, string Track, string? Album, string? ArtistMbid);

/// <summary>One page of user.getRecentTracks, newest first. NowPlaying only on the first page.</summary>
public sealed record LastFmRecentTracksPage(
    IReadOnlyList<LastFmScrobble> Scrobbles,
    LastFmNowPlaying? NowPlaying,
    int Page,
    int TotalPages,
    int Total);
