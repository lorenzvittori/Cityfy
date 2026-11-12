using CityFy.Elaboration.Clients;
using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.SubServices;

public sealed class ArtistResolveSubService(IMusicBrainzClient musicBrainz) : IArtistResolveSubService
{
    private const string SpotifyArtistUriPrefix = "spotify:artist:";
    private const string NoMatch = "none";

    public async Task<IReadOnlyDictionary<string, ResolvedArtist>> ResolveAsync(IReadOnlyList<ArtistPlays> plays, CancellationToken ct)
    {
        var request = new ResolveArtistsRequest
        {
            SpotifyArtistIds = plays.Select(p => SpotifyId(p.ArtistUri)).OfType<string>().Distinct().ToList(),
            Names = plays.Select(p => p.ArtistName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        var response = await musicBrainz.ResolveArtistsAsync(request, ct);
        var bySpotify = response.BySpotifyId.Where(r => r.MatchType != NoMatch)
            .ToDictionary(r => r.Input, StringComparer.Ordinal);
        var byName = response.ByName.Where(r => r.MatchType != NoMatch)
            .GroupBy(r => r.Input, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ResolvedArtist>();
        foreach (var p in plays)
        {
            if (SpotifyId(p.ArtistUri) is { } sid && bySpotify.TryGetValue(sid, out var s))
                result[p.ArtistName] = s;
            else if (byName.TryGetValue(p.ArtistName.Trim(), out var n)) // the resolve API trims its inputs
                result[p.ArtistName] = n;
        }
        return result;
    }

    private static string? SpotifyId(string? artistUri) =>
        artistUri != null && artistUri.StartsWith(SpotifyArtistUriPrefix, StringComparison.Ordinal)
            ? artistUri[SpotifyArtistUriPrefix.Length..]
            : null;
}
