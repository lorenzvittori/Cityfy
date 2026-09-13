using System;
using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Retrieve.Dump.Spotify.Models
{
    public class StreamingMongo
    {
        [BsonId]
        public Guid Id { get; set; } = Guid.NewGuid();

        [BsonElement("ts")]
        public DateTime Timestamp { get; set; }

        [BsonElement("platform")]
        public string? Platform { get; set; }

        [BsonElement("ms_played")]
        public int MsPlayed { get; set; }

        [BsonElement("conn_country")]
        public string? Country { get; set; }

        [BsonElement("ip_addr")]
        public string? IpAddress { get; set; }

        [BsonElement("master_metadata_track_name")]
        public string? TrackName { get; set; }

        [BsonElement("master_metadata_album_artist_name")]
        public string? ArtistName { get; set; }

        [BsonElement("master_metadata_album_album_name")]
        public string? AlbumName { get; set; }

        [BsonElement("spotify_track_uri")]
        public string? SpotifyTrackUri { get; set; }

        [BsonElement("reason_start")]
        public string? ReasonStart { get; set; }

        [BsonElement("reason_end")]
        public string? ReasonEnd { get; set; }

        [BsonElement("shuffle")]
        public bool Shuffle { get; set; }

        [BsonElement("skipped")]
        public bool Skipped { get; set; }

        [BsonElement("offline")]
        public bool Offline { get; set; }

        [BsonElement("offline_timestamp")]
        public DateTime? OfflineTimestamp { get; set; }

        [BsonElement("incognito_mode")]
        public bool IncognitoMode { get; set; }

        // metadata
        [BsonElement("_uploadId")]
        public string? UploadId { get; set; }
    }
}
