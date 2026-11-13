using CityFy.Retrieve.LastFm.Models;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.LastFm.Mappers;

/// <summary>
/// Last.fm has no track id shared with Spotify: the play key is "lastfm:{artist}|{track}" in lower
/// case, so the same scrobble read twice (history import and polling) is one document. MsPlayed and
/// Skipped stay null: Last.fm only scrobbles a track listened for at least half (or 4 minutes).
/// </summary>
public sealed class LastFmListeningMapper : ILastFmListeningMapper
{
    public const string TrackUriPrefix = "lastfm:";

    public IReadOnlyList<ListeningEvent> ToListeningEvents(IEnumerable<LastFmScrobble> scrobbles, string userId, string sourceRef) =>
        scrobbles.Select(s => NewListeningEvent(s, userId, sourceRef)).ToList();

    private static ListeningEvent NewListeningEvent(LastFmScrobble scrobble, string userId, string sourceRef)
    {
        var trackUri = TrackUri(scrobble.Artist, scrobble.Track);
        return new ListeningEvent
        {
            Id = ListeningEvent.BuildId(userId, trackUri, scrobble.ScrobbledAt),
            UserId = userId,
            // The scrobble time is the start of the play: also the start for the cross-source dedup
            PlayedAt = scrobble.ScrobbledAt,
            StartedAt = scrobble.ScrobbledAt,
            TrackUri = trackUri,
            TrackName = scrobble.Track,
            ArtistName = scrobble.Artist,
            AlbumName = scrobble.Album,
            ArtistMbid = scrobble.ArtistMbid,
            TrackMbid = scrobble.TrackMbid,
            SourceRef = sourceRef,
            Sources = [ListeningSources.LastFm]
        };
    }

    internal static string TrackUri(string artist, string track) =>
        $"{TrackUriPrefix}{artist.Trim().ToLowerInvariant()}|{track.Trim().ToLowerInvariant()}";
}
