using ServiceDefault.City;
using System.Globalization;
using System.Text;
using CityFy.Catalog.Models;
using CityFy.Catalog.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Catalog.SubServices;

/// <summary>
/// Pure transformation (no I/O): per-artist plays + artist placements -> city.
///   district = the artist's macro-genre, block = its genre (see ArtistPlacementSubService)
///   building = artist; height/width grow with the qualified plays
/// </summary>
public sealed class CityBuildSubService(IOptions<CityOptions> cityOptions) : ICityBuildSubService
{
    public const string OtherDistrictId = "other_genres";
    private const string OtherDistrictName = "Other genres";
    private const string OtherDistrictColor = "#9e9e9e";

    private readonly CityOptions _options = cityOptions.Value;

    private sealed record Placement(ArtistPlays Plays, ArtistPlacement Artist);

    public CityModel Build(
        string userId,
        DateTime? from,
        DateTime? to,
        DateTime now,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName,
        GenreFilter filter)
    {
        // Artists left out by the filter disappear: not placed, not unresolved, not in the totals
        var considered = filter.IsAll
            ? plays
            : plays.Where(p => placementsByArtistName.TryGetValue(p.ArtistName, out var a) && filter.Includes(a)).ToList();

        var placements = new List<Placement>();
        var unresolved = new List<ArtistPlays>();

        foreach (var p in considered.Where(p => p.QualifiedPlays > 0))
        {
            if (placementsByArtistName.TryGetValue(p.ArtistName, out var artist) && artist.IsPlaced)
                placements.Add(new Placement(p, artist));
            else
                unresolved.Add(p);
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
        var totalQualified = considered.Sum(p => p.QualifiedPlays);

        var districts = buildings
            .GroupBy(b => b.First.Artist.DistrictGenreId!.Value)
            .Select(d =>
            {
                var first = d.First().First.Artist;
                var districtName = first.DistrictName!;
                var districtId = Slug(districtName);
                var color = first.DistrictColor!;

                var blocks = d.GroupBy(b => b.First.Artist.BlockGenreId!.Value)
                    .Select(bl =>
                    {
                        var blockName = bl.First().First.Artist.BlockName!;
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
                                    Height = Scale(_options.MinHeight, _options.MaxHeight, Math.Log(1 + b.Qualified) / Math.Log(1 + maxQualified)),
                                    Width = Scale(_options.MinWidth, _options.MaxWidth, Math.Sqrt((double)b.Qualified / maxQualified)),
                                    Color = color,
                                    Genres = b.First.Artist.Genres.ToList()
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
            warnings.Add(ErrorCodes.CityNoPlays);                 // nothing listened in the range (or in the chosen genres)
        else if (buildings.Count == 0)
            warnings.Add(ErrorCodes.CityGenresUnavailable);       // plays, but no artist matched a genre
        else if (coverage < _options.LowCoveragePercent)
            warnings.Add(ErrorCodes.CityLowCoverage);             // the city shows only part of the listening

        return new CityModel
        {
            Warnings = warnings,
            UserId = userId,
            GeneratedAt = now,
            From = from,
            To = to,
            Summary = new CitySummary
            {
                TotalPlays = considered.Sum(p => p.Plays),
                QualifiedPlays = totalQualified,
                ListeningHours = Math.Round(considered.Sum(p => p.MsPlayed) / 3_600_000.0, 1),
                TotalArtists = considered.Count,
                PlacedArtists = buildings.Count,
                CoveragePercent = coverage,
                TopGenres = districts.SelectMany(d => d.Blocks).OrderByDescending(b => b.Score).Take(5).Select(b => b.Name).ToList()
            },
            Districts = districts,
            Unresolved = unresolved.OrderByDescending(u => u.QualifiedPlays).Take(_options.MaxUnresolvedReported)
                .Select(u => new UnresolvedArtist { Name = u.ArtistName, SpotifyArtistUri = u.ArtistUri, Plays = u.QualifiedPlays })
                .ToList()
        };
    }

    /// <summary>
    /// MusicBrainz has many top-level genres: a large listening history produces 100+ districts,
    /// which no map can show legibly. The most listened (MaxDistricts - 1) stay, the rest become
    /// one "Other genres" district whose blocks keep their genre (and their buildings their colors).
    /// Applied after the genre filter, so a filtered city keeps the districts that were chosen.
    /// </summary>
    private List<CityDistrict> MergeSmallDistricts(List<CityDistrict> districts)
    {
        if (districts.Count <= _options.MaxDistricts)
            return districts;

        var keep = districts.Take(_options.MaxDistricts - 1).ToList();
        var rest = districts.Skip(_options.MaxDistricts - 1).ToList();
        keep.Add(new CityDistrict
        {
            DistrictId = OtherDistrictId,
            Name = OtherDistrictName,
            GenreId = 0,
            Color = OtherDistrictColor,
            Score = rest.Sum(d => d.Score),
            Blocks = rest.SelectMany(d => d.Blocks).OrderByDescending(b => b.Score).ToList()
        });
        return keep;
    }

    private static double Scale(double min, double max, double ratio) =>
        Math.Round(min + (max - min) * (double.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : 1), 2);

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
