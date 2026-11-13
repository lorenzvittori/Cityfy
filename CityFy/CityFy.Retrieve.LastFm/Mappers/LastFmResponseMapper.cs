using System.Globalization;
using System.Text.Json;
using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.Mappers;

/// <summary>
/// The Last.fm JSON is a translation of its XML, with some quirks handled here:
/// - a list with a single element is an object, not an array ("track": {...});
/// - text nodes are "#text", attributes "@attr"; numbers are strings ("totalPages": "34");
/// - empty values are "" (e.g. "mbid": "");
/// - the artist is {"#text": name} with extended=0 and {"name": name} with extended=1.
/// </summary>
public sealed class LastFmResponseMapper : ILastFmResponseMapper
{
    public LastFmApiException? ToError(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var code)
            ? new LastFmApiException(ToInt(code) ?? 0, Text(root, "message") ?? "")
            : null;

    public LastFmSession ToSession(JsonElement root)
    {
        if (!root.TryGetProperty("session", out var session))
            throw new InvalidDataException("auth.getSession: 'session' missing");
        var name = Text(session, "name");
        var key = Text(session, "key");
        if (name is null || key is null)
            throw new InvalidDataException("auth.getSession: 'name' or 'key' missing");
        return new LastFmSession(name, key);
    }

    public LastFmRecentTracksPage ToRecentTracks(JsonElement root)
    {
        if (!root.TryGetProperty("recenttracks", out var recent))
            throw new InvalidDataException("user.getRecentTracks: 'recenttracks' missing");

        var scrobbles = new List<LastFmScrobble>();
        LastFmNowPlaying? nowPlaying = null;
        foreach (var track in Items(recent, "track"))
        {
            var artist = ArtistName(track);
            var name = Text(track, "name");
            if (artist is null || name is null)
                continue; // nothing to place in a city

            if (IsNowPlaying(track))
            {
                nowPlaying = new LastFmNowPlaying(artist, name, TextNode(track, "album"), ArtistMbid(track));
                continue;
            }

            // A scrobble without a date cannot be ordered nor deduplicated: skipped
            if (!track.TryGetProperty("date", out var date) || ToLong(date.TryGetProperty("uts", out var uts) ? uts : default) is not { } seconds)
                continue;

            scrobbles.Add(new LastFmScrobble(artist, name, TextNode(track, "album"), ArtistMbid(track), Text(track, "mbid"),
                DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime));
        }

        var attr = recent.TryGetProperty("@attr", out var a) ? a : default;
        return new LastFmRecentTracksPage(
            scrobbles,
            nowPlaying,
            Page: IntAttr(attr, "page") ?? 1,
            TotalPages: IntAttr(attr, "totalPages") ?? 1,
            Total: IntAttr(attr, "total") ?? scrobbles.Count);
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
            return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().ToList(),
            JsonValueKind.Object => [value],
            _ => []
        };
    }

    private static bool IsNowPlaying(JsonElement track) =>
        track.TryGetProperty("@attr", out var attr) &&
        attr.ValueKind == JsonValueKind.Object &&
        string.Equals(Text(attr, "nowplaying"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? ArtistName(JsonElement track) =>
        track.TryGetProperty("artist", out var artist)
            ? artist.ValueKind == JsonValueKind.Object ? Text(artist, "name") ?? Text(artist, "#text") : NonEmpty(artist)
            : null;

    private static string? ArtistMbid(JsonElement track) =>
        track.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object ? Text(artist, "mbid") : null;

    /// <summary>{"album": {"#text": "..."}} or a plain string.</summary>
    private static string? TextNode(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var node)
            ? node.ValueKind == JsonValueKind.Object ? Text(node, "#text") : NonEmpty(node)
            : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? NonEmpty(value) : null;

    private static string? NonEmpty(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s.Trim() : null,
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

    private static int? IntAttr(JsonElement attr, string name) =>
        attr.ValueKind == JsonValueKind.Object && attr.TryGetProperty(name, out var value) ? ToInt(value) : null;

    private static int? ToInt(JsonElement value) => ToLong(value) is { } l and >= int.MinValue and <= int.MaxValue ? (int)l : null;

    private static long? ToLong(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
}
