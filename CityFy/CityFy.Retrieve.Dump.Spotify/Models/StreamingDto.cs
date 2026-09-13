using System;
using System.Text.Json.Serialization;

namespace CityFy.Retrieve.Dump.Spotify.Models
{
    public class StreamingDto
    {
        [JsonPropertyName("ts")]
        public DateTime Ts { get; set; }

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("ms_played")]
        public int? MsPlayed { get; set; }

        [JsonPropertyName("conn_country")]
        public string? ConnCountry { get; set; }

        [JsonPropertyName("ip_addr")]
        public string? IpAddr { get; set; }

        [JsonPropertyName("master_metadata_track_name")]
        public string? TrackName { get; set; }

        [JsonPropertyName("master_metadata_album_artist_name")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("master_metadata_album_album_name")]
        public string? AlbumName { get; set; }

        [JsonPropertyName("spotify_track_uri")]
        public string? SpotifyTrackUri { get; set; }

        [JsonPropertyName("episode_name")]
        public string? EpisodeName { get; set; }

        [JsonPropertyName("episode_show_name")]
        public string? EpisodeShowName { get; set; }

        [JsonPropertyName("spotify_episode_uri")]
        public string? SpotifyEpisodeUri { get; set; }

        [JsonPropertyName("audiobook_title")]
        public string? AudiobookTitle { get; set; }

        [JsonPropertyName("audiobook_uri")]
        public string? AudiobookUri { get; set; }

        [JsonPropertyName("audiobook_chapter_uri")]
        public string? AudiobookChapterUri { get; set; }

        [JsonPropertyName("audiobook_chapter_title")]
        public string? AudiobookChapterTitle { get; set; }

        [JsonPropertyName("reason_start")]
        public string? ReasonStart { get; set; }

        [JsonPropertyName("reason_end")]
        public string? ReasonEnd { get; set; }

        [JsonPropertyName("shuffle")]
        public bool? Shuffle { get; set; }

        [JsonPropertyName("skipped")]
        public bool? Skipped { get; set; }

        [JsonPropertyName("offline")]
        public bool? Offline { get; set; }

        [JsonPropertyName("offline_timestamp")]
        public DateTime? OfflineTimestamp { get; set; }

        [JsonPropertyName("incognito_mode")]
        public bool? IncognitoMode { get; set; }
    }
}
