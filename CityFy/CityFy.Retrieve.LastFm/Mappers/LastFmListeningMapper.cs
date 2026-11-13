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
        scrobbles.Select(s =>
        {
            var trackUri = TrackUri(s.Artist, s.Track);
            return new ListeningEvent
            {
                Id = ListeningEvent.BuildId(userId, trackUri, s.ScrobbledAt),
                UserId = userId,
                // The scrobble time is the start of the play: also the start for the cross-source dedup
                PlayedAt = s.ScrobbledAt,
                StartedAt = s.ScrobbledAt,
                TrackUri = trackUri,
                TrackName = s.Track,
                ArtistName = s.Artist,
                AlbumName = s.Album,
                ArtistMbid = s.ArtistMbid,
                TrackMbid = s.TrackMbid,
                SourceRef = sourceRef,
                Sources = [ListeningSources.LastFm]
            };
        }).ToList();

    internal static string TrackUri(string artist, string track) =>
        $"{TrackUriPrefix}{artist.Trim().ToLowerInvariant()}|{track.Trim().ToLowerInvariant()}";
}
