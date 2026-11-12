using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.Listening;

public static class ListeningSources
{
    public const string Dump = "dump";
    public const string Api = "api";
}

/// <summary>
/// One music play, shared contract between the retrieve services (writers) and
/// Catalog (reader). Stored in the 'listening_events' collection.
/// </summary>
public sealed class ListeningEvent
{
    public const string CollectionName = "listening_events";

    /// <summary>Deterministic key, see <see cref="BuildId"/>.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>When the play ended (UTC). Spotify export 'ts' / API 'played_at'.</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime PlayedAt { get; set; }

    /// <summary>Milliseconds actually listened. Known only from the dump (null from the API).</summary>
    public int? MsPlayed { get; set; }

    /// <summary>Known only from the dump.</summary>
    public bool? Skipped { get; set; }

    /// <summary>spotify:track:{id}</summary>
    public string TrackUri { get; set; } = null!;

    public string? TrackName { get; set; }

    /// <summary>Primary artist name (dump: album artist; API: first track artist).</summary>
    public string? ArtistName { get; set; }

    /// <summary>spotify:artist:{id}. Known only from the API.</summary>
    public string? ArtistUri { get; set; }

    public string? AlbumName { get; set; }

    /// <summary>Playlist/album the play came from. Known only from the API.</summary>
    public string? ContextUri { get; set; }

    /// <summary>Sources that reported this play (<see cref="ListeningSources"/>).</summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>uploadId (dump) or jobId (API) of the first import.</summary>
    public string? SourceRef { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ImportedAt { get; set; }

    /// <summary>
    /// "{userId}:{trackUri}:{playedAt truncated to the second}". The dump has second
    /// precision and the API millisecond precision: truncating makes the same play
    /// coming from both sources collapse onto one document.
    /// </summary>
    public static string BuildId(string userId, string trackUri, DateTime playedAt) =>
        $"{userId}:{trackUri}:{playedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}";
}
