using ServiceDefault.City;
using System.Globalization;
using System.Text;
using CityFy.Elaboration.Models;
using CityFy.Elaboration.Options;
using ServiceDefault.Errors;

namespace CityFy.Elaboration.Services;

/// <summary>
/// Pure transformation (no I/O): per-artist plays + artist genres + genre hierarchy -> city.
///   district = macro-genre (root of the subgenre hierarchy that collects most of the artist's genre votes)
///   block    = the artist's most voted genre inside that macro-genre
///   building = artist; height/width grow with the qualified plays
/// </summary>
public sealed class CityBuilder(CityOptions options)
{
    private sealed record Placement(ArtistPlays Plays, ResolvedArtist Artist, int MacroGenreId, int BlockGenreId);

    public CityModel Build(
        string userId,
        string period,
        DateTime? from,
        DateTime now,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ResolvedArtist> resolvedByArtistName,
        GenreHierarchy hierarchy)
    {
        var placements = new List<Placement>();
        var unresolved = new List<ArtistPlays>();

        foreach (var p in plays.Where(p => p.QualifiedPlays > 0))
        {
            if (!resolvedByArtistName.TryGetValue(p.ArtistName, out var artist) ||
                artist.Mbid == null ||
                artist.Genres.Count == 0 ||
                ChooseMacro(artist, hierarchy) is not { } macro)
            {
                unresolved.Add(p);
                continue;
            }

            placements.Add(new Placement(p, artist, macro, ChooseBlock(artist, macro, hierarchy)));
        }

        // The same MusicBrainz artist can come from different names/URIs: one building
        var buildings = placements
            .GroupBy(x => x.Artist.Mbid!)
            .Select(g => new
            {
                First = g.OrderByDescending(x => x.Plays.QualifiedPlays).First(),
                Plays = g.Sum(x => x.Plays.Plays),
                Qualified = g.Sum(x => x.Plays.QualifiedPlays),
                Ms = g.Sum(x => x.Plays.MsPlayed),
                // Spotify page: from the plays (API retrieve), else the one linked in MusicBrainz (export)
                Uri = g.Select(x => x.Plays.ArtistUri).FirstOrDefault(u => u != null)
                      ?? g.Select(x => x.Artist.SpotifyArtistId).Where(id => !string.IsNullOrEmpty(id))
                          .Select(id => $"spotify:artist:{id}").FirstOrDefault()
            })
            .ToList();

        var maxQualified = buildings.Count > 0 ? buildings.Max(b => b.Qualified) : 1;
        var placedQualified = buildings.Sum(b => b.Qualified);
        var totalQualified = plays.Sum(p => p.QualifiedPlays);

        var districts = buildings
            .GroupBy(b => b.First.MacroGenreId)
            .Select(d =>
            {
                var districtName = hierarchy.Get(d.Key)?.Name ?? $"genre {d.Key}";
                var districtId = Slug(districtName);
                var color = ColorFor(districtName);

                var blocks = d.GroupBy(b => b.First.BlockGenreId)
                    .Select(bl =>
                    {
                        var blockName = hierarchy.Get(bl.Key)?.Name ?? $"genre {bl.Key}";
                        return new CityBlock
                        {
                            BlockId = $"{districtId}__{Slug(blockName)}",
                            Name = blockName,
                            GenreId = bl.Key,
                            Score = bl.Sum(b => b.Qualified),
                            Buildings = bl.OrderByDescending(b => b.Qualified)
                                .Select(b => new CityBuilding
                                {
                                    BuildingId = b.First.Artist.Mbid!,
                                    Name = b.First.Artist.Name ?? b.First.Plays.ArtistName,
                                    SpotifyArtistUri = b.Uri,
                                    Plays = b.Plays,
                                    QualifiedPlays = b.Qualified,
                                    ListeningMinutes = Math.Round(b.Ms / 60_000.0, 1),
                                    Height = Scale(options.MinHeight, options.MaxHeight, Math.Log(1 + b.Qualified) / Math.Log(1 + maxQualified)),
                                    Width = Scale(options.MinWidth, options.MaxWidth, Math.Sqrt((double)b.Qualified / maxQualified)),
                                    Color = color,
                                    Genres = b.First.Artist.Genres.Select(g => g.Name).ToList()
                                })
                                .ToList()
                        };
                    })
                    .OrderByDescending(bl => bl.Score)
                    .ToList();

                return new CityDistrict
                {
                    DistrictId = districtId,
                    Name = districtName,
                    GenreId = d.Key,
                    Color = color,
                    Score = blocks.Sum(b => b.Score),
                    Blocks = blocks
                };
            })
            .OrderByDescending(d => d.Score)
            .ToList();

        districts = MergeSmallDistricts(districts);

        var maxArtists = districts.Count > 0 ? districts.Max(d => d.Blocks.Sum(b => b.Buildings.Count)) : 1;
        foreach (var d in districts)
        {
            d.Density = Math.Round((double)d.Blocks.Sum(b => b.Buildings.Count) / maxArtists, 3);
            d.ListeningIntensity = placedQualified > 0 ? Math.Round(100.0 * d.Score / placedQualified, 2) : 0;
        }

        var coverage = totalQualified > 0 ? Math.Round(100.0 * placedQualified / totalQualified, 2) : 0;
        var warnings = new List<string>();
        if (totalQualified == 0)
            warnings.Add(ErrorCodes.CityNoPlays);                 // nothing listened in the period
        else if (buildings.Count == 0)
            warnings.Add(ErrorCodes.CityGenresUnavailable);       // plays, but no artist matched a genre
        else if (coverage < options.LowCoveragePercent)
            warnings.Add(ErrorCodes.CityLowCoverage);             // the city shows only part of the listening

        return new CityModel
        {
            Id = CityModel.BuildId(userId, period),
            Warnings = warnings,
            UserId = userId,
            Period = period,
            GeneratedAt = now,
            From = from,
            Summary = new CitySummary
            {
                TotalPlays = plays.Sum(p => p.Plays),
                QualifiedPlays = totalQualified,
                ListeningHours = Math.Round(plays.Sum(p => p.MsPlayed) / 3_600_000.0, 1),
                TotalArtists = plays.Count,
                PlacedArtists = buildings.Count,
                CoveragePercent = coverage,
                TopGenres = districts.SelectMany(d => d.Blocks).OrderByDescending(b => b.Score).Take(5).Select(b => b.Name).ToList()
            },
            Districts = districts,
            Unresolved = unresolved.OrderByDescending(u => u.QualifiedPlays).Take(options.MaxUnresolvedReported)
                .Select(u => new UnresolvedArtist { Name = u.ArtistName, SpotifyArtistUri = u.ArtistUri, Plays = u.QualifiedPlays })
                .ToList()
        };
    }

    public const string OtherDistrictId = "other_genres";

    /// <summary>
    /// MusicBrainz has many top-level genres: a large listening history produces 100+ districts,
    /// which no map can show legibly. The most listened (MaxDistricts - 1) stay, the rest become
    /// one "Other genres" district whose blocks keep their genre (and their buildings their colors).
    /// </summary>
    private List<CityDistrict> MergeSmallDistricts(List<CityDistrict> districts)
    {
        if (districts.Count <= options.MaxDistricts)
            return districts;

        var keep = districts.Take(options.MaxDistricts - 1).ToList();
        var rest = districts.Skip(options.MaxDistricts - 1).ToList();
        keep.Add(new CityDistrict
        {
            DistrictId = OtherDistrictId,
            Name = "Other genres",
            GenreId = 0,
            Color = "#9e9e9e",
            Score = rest.Sum(d => d.Score),
            Blocks = rest.SelectMany(d => d.Blocks).OrderByDescending(b => b.Score).ToList()
        });
        return keep;
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

    private static double Scale(double min, double max, double ratio) =>
        Math.Round(min + (max - min) * (double.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : 1), 2);

    private string ColorFor(string macroName)
    {
        if (options.Palette.TryGetValue(macroName, out var fixedColor))
            return fixedColor;

        // FNV-1a: stable across runs (string.GetHashCode is randomized per process)
        uint hash = 2166136261;
        foreach (var c in macroName.ToLowerInvariant())
            hash = (hash ^ c) * 16777619;

        return HslToHex(hash % 360, 0.55, 0.55);
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

    // "hip hop" -> "hip_hop", "drum and bass" -> "drum_and_bass"
    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        return string.Join('_', sb.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
    }
}
