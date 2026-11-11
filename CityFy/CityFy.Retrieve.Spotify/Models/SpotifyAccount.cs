using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Retrieve.Spotify.Models
{
    /// <summary>
    /// A Spotify account linked through the OAuth login ('spotify_accounts' collection).
    /// The refresh token is stored encrypted with ASP.NET Core Data Protection; access tokens
    /// are never persisted (short-lived, kept in memory only).
    /// </summary>
    public class SpotifyAccount
    {
        [BsonId]
        public string UserId { get; set; } = null!;

        public string? DisplayName { get; set; }

        public string ProtectedRefreshToken { get; set; } = null!;

        public string? Scope { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime LinkedAt { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime UpdatedAt { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime? LastRetrieveAt { get; set; }

        /// <summary>Most recent play already imported: cursor for the incremental retrieve ('after').</summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime? LastPlayedAt { get; set; }
    }
}
