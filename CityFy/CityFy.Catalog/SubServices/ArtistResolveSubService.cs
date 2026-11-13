using CityFy.Catalog.Clients;
using CityFy.Catalog.Models;

namespace CityFy.Catalog.SubServices;

/// <summary>
/// Artist name → MusicBrainz artist. The most precise key the plays carry wins: Spotify artist id
/// (link stored in MusicBrainz), then the MusicBrainz id given by Last.fm, then the name (homonyms:
/// the one with most genre votes).
/// </summary>
public sealed class ArtistResolveSubService(IMusicBrainzClient musicBrainz) : IArtistResolveSubService
{
    private const string SpotifyArtistUriPrefix = "spotify:artist:";
    private const string NoMatch = "none";

    public async Task<IReadOnlyDictionary<string, ResolvedArtist>> ResolveAsync(IReadOnlyList<ArtistPlays> plays, CancellationToken ct)
    {
        var request = new ResolveArtistsRequest
        {
            SpotifyArtistIds = plays.Select(p => SpotifyId(p.ArtistUri)).OfType<string>().Distinct().ToList(),
            Names = plays.Select(p => p.ArtistName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Mbids = plays.Select(p => p.ArtistMbid).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        var response = await musicBrainz.ResolveArtistsAsync(request, ct);
        var bySpotify = Matched(response.BySpotifyId, StringComparer.Ordinal);
        var byMbid = Matched(response.ByMbid ?? [], StringComparer.OrdinalIgnoreCase);
        var byName = Matched(response.ByName, StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ResolvedArtist>();
        foreach (var p in plays)
        {
            if (SpotifyId(p.ArtistUri) is { } sid && bySpotify.TryGetValue(sid, out var s))
                result[p.ArtistName] = s;
            else if (p.ArtistMbid is { } mbid && byMbid.TryGetValue(mbid, out var m))
                result[p.ArtistName] = m;
            else if (byName.TryGetValue(p.ArtistName.Trim(), out var n)) // the resolve API trims its inputs
                result[p.ArtistName] = n;
        }
        return result;
    }

    private static Dictionary<string, ResolvedArtist> Matched(IEnumerable<ResolvedArtist> resolved, StringComparer comparer) =>
        resolved.Where(IsMatched)
            .GroupBy(r => r.Input, comparer)
            .ToDictionary(g => g.Key, g => g.First(), comparer);

    private static bool IsMatched(ResolvedArtist artist) => artist.MatchType != NoMatch;

    private static string? SpotifyId(string? artistUri) =>
        artistUri != null && artistUri.StartsWith(SpotifyArtistUriPrefix, StringComparison.Ordinal)
            ? artistUri[SpotifyArtistUriPrefix.Length..]
            : null;
}
