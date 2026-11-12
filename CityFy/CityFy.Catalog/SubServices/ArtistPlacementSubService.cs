using CityFy.Catalog.Models;
using CityFy.Catalog.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Catalog.SubServices;

/// <summary>
///   district = macro-genre (root of the subgenre hierarchy that collects most of the artist's genre votes)
///   block    = the artist's most voted genre inside that macro-genre
///   color    = of the district: fixed in CityOptions.Palette, else a hue hashed from its name
/// </summary>
public sealed class ArtistPlacementSubService(IOptions<CityOptions> cityOptions) : IArtistPlacementSubService
{
    private const double Saturation = 0.55;
    private const double Lightness = 0.55;

    private readonly CityOptions _options = cityOptions.Value;

    public IReadOnlyList<ArtistPlacement> Place(
        string userId,
        string catalogVersion,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ResolvedArtist> resolvedByArtistName,
        GenreHierarchy hierarchy)
    {
        var result = new List<ArtistPlacement>(plays.Count);
        foreach (var p in plays)
        {
            var placement = new ArtistPlacement
            {
                Id = ArtistPlacement.BuildId(userId, catalogVersion, p.ArtistName),
                UserId = userId,
                CatalogVersion = catalogVersion,
                ArtistName = p.ArtistName
            };
            result.Add(placement);

            if (!resolvedByArtistName.TryGetValue(p.ArtistName, out var artist) || artist.Mbid == null)
                continue;

            placement.Mbid = artist.Mbid;
            placement.Name = artist.Name;
            placement.SpotifyArtistId = string.IsNullOrEmpty(artist.SpotifyArtistId) ? null : artist.SpotifyArtistId;
            placement.Genres = artist.Genres.Select(g => g.Name).ToList();

            if (artist.Genres.Count == 0 || ChooseMacro(artist, hierarchy) is not { } macro)
                continue;

            var block = ChooseBlock(artist, macro, hierarchy);
            placement.DistrictGenreId = macro;
            placement.DistrictName = hierarchy.Get(macro)?.Name ?? $"genre {macro}";
            placement.DistrictColor = ColorFor(placement.DistrictName);
            placement.BlockGenreId = block;
            placement.BlockName = hierarchy.Get(block)?.Name ?? $"genre {block}";
        }
        return result;
    }

    // Root collecting most of the artist's genre votes (a genre with k roots gives 1/k of its votes to each)
    private static int? ChooseMacro(ResolvedArtist artist, GenreHierarchy hierarchy)
    {
        var scores = new Dictionary<int, double>();
        foreach (var g in artist.Genres)
        {
            var roots = hierarchy.GetRoots(g.Id);
            foreach (var r in roots)
                scores[r] = scores.GetValueOrDefault(r) + (double)Math.Max(g.Votes, 1) / roots.Count;
        }

        return scores.Count == 0 ? null : scores.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    // Most voted genre of the artist that belongs to the chosen macro-genre (Genres are sorted by votes)
    private static int ChooseBlock(ResolvedArtist artist, int macro, GenreHierarchy hierarchy) =>
        artist.Genres.FirstOrDefault(g => hierarchy.GetRoots(g.Id).Contains(macro))?.Id ?? macro;

    private string ColorFor(string macroName)
    {
        if (_options.Palette.TryGetValue(macroName, out var fixedColor))
            return fixedColor;

        // FNV-1a: stable across runs (string.GetHashCode is randomized per process)
        uint hash = 2166136261;
        foreach (var c in macroName.ToLowerInvariant())
            hash = (hash ^ c) * 16777619;

        return HslToHex(hash % 360, Saturation, Lightness);
    }

    private static string HslToHex(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return $"#{(int)Math.Round((r + m) * 255):x2}{(int)Math.Round((g + m) * 255):x2}{(int)Math.Round((b + m) * 255):x2}";
    }
}
