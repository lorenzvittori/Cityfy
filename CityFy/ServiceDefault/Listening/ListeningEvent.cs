using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.Listening;

public static class ListeningSources
{
    /// <summary>Spotify export (zip).</summary>
    public const string Dump = "dump";
    public const string LastFm = "lastfm";
}

/// <summary>
/// One music play, shared contract between the retrieve services (writers) and
/// Catalog (reader). Stored in the 'listening_events' collection.
/// The same play reported by two sources is one document:
/// - same source key (<see cref="TrackUri"/>, e.g. the same export uploaded twice): same <see cref="Id"/>;
/// - different keys (Last.fm and the Spotify export): same <see cref="MatchKey"/> and a start
///   (<see cref="StartedAt"/>) within <see cref="MatchTolerance"/>, merged by the store.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class ListeningEvent
{
    public const string CollectionName = "listening_events";

    /// <summary>Two sources report the start of the same play at most this far apart.</summary>
    public static readonly TimeSpan MatchTolerance = TimeSpan.FromMinutes(2);

    /// <summary>Deterministic key, see <see cref="BuildId"/>.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>
    /// When the play ended (UTC): Spotify export 'ts'. Last.fm only gives the scrobble
    /// time, which is when the play started: the cross-source dedup (PIANO_LASTFM.md L3) has a tolerance.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime PlayedAt { get; set; }

    /// <summary>Milliseconds actually listened. Known only from the export (null from Last.fm, which scrobbles only real plays).</summary>
    public int? MsPlayed { get; set; }

    /// <summary>Known only from the dump.</summary>
    public bool? Skipped { get; set; }

    /// <summary>spotify:track:{id}, or lastfm:{artist}|{track} (lower case) for Last.fm scrobbles.</summary>
    public string TrackUri { get; set; } = null!;

    public string? TrackName { get; set; }

    /// <summary>Primary artist name (export: album artist; Last.fm: the scrobbled artist).</summary>
    public string? ArtistName { get; set; }

    /// <summary>spotify:artist:{id}. Written only by the Spotify API retrieve, removed: kept for the artist resolve, which still uses it when present.</summary>
    public string? ArtistUri { get; set; }

    public string? AlbumName { get; set; }

    /// <summary>Playlist/album the play came from. Written only by the removed Spotify API retrieve.</summary>
    public string? ContextUri { get; set; }

    /// <summary>
    /// When the play started (UTC): export 'ts' minus 'ms_played', Last.fm scrobble time. Null when
    /// unknown (plays stored before the field existed): such a play is matched only by <see cref="Id"/>.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? StartedAt { get; set; }

    /// <summary>Normalized "artist|track" for the cross-source dedup (<see cref="BuildMatchKey"/>). Set by the store when missing.</summary>
    public string? MatchKey { get; set; }

    /// <summary>MusicBrainz artist id, when the source gives it (Last.fm): the artist resolve uses it before the name.</summary>
    public string? ArtistMbid { get; set; }

    /// <summary>MusicBrainz recording id, when the source gives it (Last.fm).</summary>
    public string? TrackMbid { get; set; }

    /// <summary>Sources that reported this play (<see cref="ListeningSources"/>).</summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>uploadId (export), import id or "poll-…" (Last.fm) of the first import.</summary>
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

    /// <summary>
    /// "artist|track", the same for the names written by Spotify and by Last.fm: lower case, without
    /// accents, "&amp;" as "and", punctuation as spaces, and without the featured artists of the track
    /// ("Song (feat. X)", "Song [with X]", "Song ft. X"). Null when artist or track is missing.
    /// </summary>
    public static string? BuildMatchKey(string? artist, string? track)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
            return null;
        var a = NormalizeName(artist);
        var t = NormalizeName(FeaturingInBrackets.Replace(FeaturingAtTheEnd.Replace(track, ""), ""));
        return a.Length == 0 || t.Length == 0 ? null : $"{a}|{t}";
    }

    private static readonly Regex FeaturingInBrackets = new(
        @"\s*[\(\[](feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FeaturingAtTheEnd = new(
        @"\s+(feat\.?|ft\.?|featuring)\s.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string NormalizeName(string name)
    {
        var text = new StringBuilder(name.Length);
        var lastWasSpace = true;
        foreach (var c in name.Replace("&", " and ").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // accents
            if (char.IsLetterOrDigit(c))
            {
                text.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                text.Append(' ');
                lastWasSpace = true;
            }
        }
        return text.ToString().Trim();
    }
}
