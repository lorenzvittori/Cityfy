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
    private const string SpotifyArtistUriPrefix = "spotify:artist:";
    private const int TopGenresCount = 5;

    private readonly CityOptions _options = cityOptions.Value;

    /// <summary>The plays of an artist name and where its artist is placed.</summary>
    private sealed record Placement(ArtistPlays Plays, ArtistPlacement Artist);

    /// <summary>One building: every name/URI that resolved to the same MusicBrainz artist, summed.</summary>
    private sealed record ArtistTotals(Placement Main, long Plays, long Qualified, long Ms, string? SpotifyUri)
    {
        /// <summary>The name most listened gives name and genres; plays are summed over all the names.</summary>
        public static ArtistTotals From(IGrouping<string, Placement> sameArtist) =>
            new(sameArtist.MaxBy(PlacementQualifiedPlays)!,
                sameArtist.Sum(PlacementPlays),
                sameArtist.Sum(PlacementQualifiedPlays),
                sameArtist.Sum(PlacementMs),
                SpotifyUriOf(sameArtist));

        public int DistrictGenreId => Main.Artist.DistrictGenreId!.Value;
        public int BlockGenreId => Main.Artist.BlockGenreId!.Value;

        private static long PlacementPlays(Placement p) => p.Plays.Plays;
        private static long PlacementQualifiedPlays(Placement p) => p.Plays.QualifiedPlays;
        private static long PlacementMs(Placement p) => p.Plays.MsPlayed;

        /// <summary>Spotify page: from the plays when they carry it, else the one linked in MusicBrainz.</summary>
        private static string? SpotifyUriOf(IEnumerable<Placement> sameArtist)
        {
            string? linked = null;
            foreach (var p in sameArtist)
            {
                if (p.Plays.ArtistUri is { } uri)
                    return uri;
                if (linked is null && !string.IsNullOrEmpty(p.Artist.SpotifyArtistId))
                    linked = SpotifyArtistUriPrefix + p.Artist.SpotifyArtistId;
            }
            return linked;
        }
    }

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
        var considered = Considered(plays, placementsByArtistName, filter);

        var placements = new List<Placement>();
        var unresolved = new List<ArtistPlays>();
        foreach (var p in considered)
        {
            if (p.QualifiedPlays == 0)
                continue;
            if (placementsByArtistName.TryGetValue(p.ArtistName, out var artist) && artist.IsPlaced)
                placements.Add(new Placement(p, artist));
            else
                unresolved.Add(p);
        }

        // The same MusicBrainz artist can come from different names/URIs: one building
        var buildings = placements.GroupBy(ArtistMbid).Select(ArtistTotals.From).ToList();

        var maxQualified = buildings.Count > 0 ? buildings.Max(QualifiedOf) : 1;
        var placedQualified = buildings.Sum(QualifiedOf);
        var totalQualified = considered.Sum(QualifiedOf);

        var districts = buildings
            .GroupBy(DistrictGenreIdOf)
            .Select(d => NewDistrict(d, maxQualified))
            .OrderByDescending(ScoreOf)
            .ToList();
        districts = MergeSmallDistricts(districts);
        SetDensityAndIntensity(districts, placedQualified);

        var coverage = totalQualified > 0 ? Math.Round(100.0 * placedQualified / totalQualified, 2) : 0;
        return new CityModel
        {
            Warnings = Warnings(totalQualified, buildings.Count, coverage),
            UserId = userId,
            GeneratedAt = now,
            From = from,
            To = to,
            Summary = NewSummary(considered, totalQualified, buildings.Count, coverage, districts),
            Districts = districts,
            Unresolved = unresolved.OrderByDescending(QualifiedOf).Take(_options.MaxUnresolvedReported).Select(NewUnresolvedArtist).ToList()
        };
    }

    private static IReadOnlyList<ArtistPlays> Considered(
        IReadOnlyList<ArtistPlays> plays, IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName, GenreFilter filter)
    {
        if (filter.IsAll)
            return plays;
        var considered = new List<ArtistPlays>();
        foreach (var p in plays)
            if (placementsByArtistName.TryGetValue(p.ArtistName, out var artist) && filter.Includes(artist))
                considered.Add(p);
        return considered;
    }

    // --- factories ---------------------------------------------------------------------

    private CityDistrict NewDistrict(IGrouping<int, ArtistTotals> district, long maxQualified)
    {
        var artist = district.First().Main.Artist;
        var districtId = Slug(artist.DistrictName!);
        var blocks = district
            .GroupBy(BlockGenreIdOf)
            .Select(b => NewBlock(b, districtId, artist.DistrictColor!, maxQualified))
            .OrderByDescending(ScoreOf)
            .ToList();
        return new CityDistrict
        {
            DistrictId = districtId,
            Name = artist.DistrictName!,
            GenreId = district.Key,
            Color = artist.DistrictColor!,
            Score = blocks.Sum(ScoreOf),
            Blocks = blocks
        };
    }

    private CityBlock NewBlock(IGrouping<int, ArtistTotals> block, string districtId, string color, long maxQualified)
    {
        var blockName = block.First().Main.Artist.BlockName!;
        return new CityBlock
        {
            BlockId = $"{districtId}__{Slug(blockName)}",
            Name = blockName,
            GenreId = block.Key,
            Score = block.Sum(QualifiedOf),
            Buildings = block.OrderByDescending(QualifiedOf).Select(b => NewBuilding(b, color, maxQualified)).ToList()
        };
    }

    private CityBuilding NewBuilding(ArtistTotals artist, string color, long maxQualified) => new()
    {
        BuildingId = artist.Main.Artist.Mbid!,
        Name = artist.Main.Artist.Name ?? artist.Main.Plays.ArtistName,
        SpotifyArtistUri = artist.SpotifyUri,
        Plays = artist.Plays,
        QualifiedPlays = artist.Qualified,
        ListeningMinutes = Math.Round(artist.Ms / 60_000.0, 1),
        Height = Scale(_options.MinHeight, _options.MaxHeight, Math.Log(1 + artist.Qualified) / Math.Log(1 + maxQualified)),
        Width = Scale(_options.MinWidth, _options.MaxWidth, Math.Sqrt((double)artist.Qualified / maxQualified)),
        Color = color,
        Genres = artist.Main.Artist.Genres.ToList()
    };

    private static CitySummary NewSummary(
        IReadOnlyList<ArtistPlays> considered, long totalQualified, int placedArtists, double coverage, List<CityDistrict> districts) => new()
    {
        TotalPlays = considered.Sum(PlaysOf),
        QualifiedPlays = totalQualified,
        ListeningHours = Math.Round(considered.Sum(MsOf) / 3_600_000.0, 1),
        TotalArtists = considered.Count,
        PlacedArtists = placedArtists,
        CoveragePercent = coverage,
        TopGenres = districts.SelectMany(BlocksOf).OrderByDescending(ScoreOf).Take(TopGenresCount).Select(NameOf).ToList()
    };

    private static UnresolvedArtist NewUnresolvedArtist(ArtistPlays plays) =>
        new() { Name = plays.ArtistName, SpotifyArtistUri = plays.ArtistUri, Plays = plays.QualifiedPlays };

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
        keep.Add(NewOtherDistrict(districts.Skip(_options.MaxDistricts - 1).ToList()));
        return keep;
    }

    private static CityDistrict NewOtherDistrict(List<CityDistrict> rest) => new()
    {
        DistrictId = OtherDistrictId,
        Name = OtherDistrictName,
        GenreId = 0,
        Color = OtherDistrictColor,
        Score = rest.Sum(ScoreOf),
        Blocks = rest.SelectMany(BlocksOf).OrderByDescending(ScoreOf).ToList()
    };

    // --- rules -------------------------------------------------------------------------

    private static void SetDensityAndIntensity(List<CityDistrict> districts, long placedQualified)
    {
        var maxArtists = districts.Count > 0 ? districts.Max(ArtistCountOf) : 1;
        foreach (var d in districts)
        {
            d.Density = Math.Round((double)ArtistCountOf(d) / maxArtists, 3);
            d.ListeningIntensity = placedQualified > 0 ? Math.Round(100.0 * d.Score / placedQualified, 2) : 0;
        }
    }

    private List<string> Warnings(long totalQualified, int placedArtists, double coverage)
    {
        if (totalQualified == 0)
            return [ErrorCodes.CityNoPlays];              // nothing listened in the range (or in the chosen genres)
        if (placedArtists == 0)
            return [ErrorCodes.CityGenresUnavailable];    // plays, but no artist matched a genre
        if (coverage < _options.LowCoveragePercent)
            return [ErrorCodes.CityLowCoverage];          // the city shows only part of the listening
        return [];
    }

    private static int ArtistCountOf(CityDistrict district) => district.Blocks.Sum(BuildingCountOf);

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

    // --- selectors (LINQ keys and values) ----------------------------------------------

    private static string ArtistMbid(Placement p) => p.Artist.Mbid!;
    private static int DistrictGenreIdOf(ArtistTotals a) => a.DistrictGenreId;
    private static int BlockGenreIdOf(ArtistTotals a) => a.BlockGenreId;
    private static long QualifiedOf(ArtistTotals a) => a.Qualified;
    private static long QualifiedOf(ArtistPlays p) => p.QualifiedPlays;
    private static long PlaysOf(ArtistPlays p) => p.Plays;
    private static long MsOf(ArtistPlays p) => p.MsPlayed;
    private static long ScoreOf(CityDistrict d) => d.Score;
    private static long ScoreOf(CityBlock b) => b.Score;
    private static string NameOf(CityBlock b) => b.Name;
    private static IEnumerable<CityBlock> BlocksOf(CityDistrict d) => d.Blocks;
    private static int BuildingCountOf(CityBlock b) => b.Buildings.Count;
}
