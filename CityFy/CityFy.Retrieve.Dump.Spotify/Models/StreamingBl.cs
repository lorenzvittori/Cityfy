using System;

namespace CityFy.Retrieve.Dump.Spotify.Models
{
    public class StreamingBl
    {
        public DateTime Timestamp { get; set; }
        public string? Platform { get; set; }
        public int MsPlayed { get; set; }
        public string? Country { get; set; }
        public string? IpAddress { get; set; }
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? AlbumName { get; set; }
        public string? SpotifyTrackUri { get; set; }
        public string? ReasonStart { get; set; }
        public string? ReasonEnd { get; set; }
        public bool Shuffle { get; set; }
        public bool Skipped { get; set; }
        public bool Offline { get; set; }
        public DateTime? OfflineTimestamp { get; set; }
        public bool IncognitoMode { get; set; }
    }
}
