using CityFy.Catalog.Models;
using ServiceDefault.City;

namespace CityFy.Catalog.SubServices;

public sealed class GenreOptionsSubService : IGenreOptionsSubService
{
    /// <summary>The qualified plays of an artist name, with the placement of its artist.</summary>
    private sealed record PlacedPlays(ArtistPlays Plays, ArtistPlacement Artist);

    public IReadOnlyList<GenreOption> List(IReadOnlyList<ArtistPlays> plays, IReadOnlyDictionary<string, ArtistPlacement> placementsByArtistName)
    {
        var placed = new List<PlacedPlays>();
        foreach (var p in plays)
            if (p.QualifiedPlays > 0 && placementsByArtistName.GetValueOrDefault(p.ArtistName) is { IsPlaced: true } artist)
                placed.Add(new PlacedPlays(p, artist));

        return placed
            .GroupBy(DistrictGenreIdOf)
            .Select(NewGenreOption)
            .OrderByDescending(QualifiedPlaysOf)
            .ThenBy(NameOf, StringComparer.Ordinal)
            .ToList();
    }

    // --- factories ---------------------------------------------------------------------

    private static GenreOption NewGenreOption(IGrouping<int, PlacedPlays> district)
    {
        var artist = district.First().Artist;
        var subgenres = district
            .GroupBy(BlockGenreIdOf)
            .Select(NewSubgenreOption)
            .OrderByDescending(QualifiedPlaysOf)
            .ThenBy(NameOf, StringComparer.Ordinal)
            .ToList();
        return new GenreOption(district.Key, artist.DistrictName!, artist.DistrictColor!,
            district.Sum(QualifiedPlaysOf), ArtistCount(district), subgenres);
    }

    private static SubgenreOption NewSubgenreOption(IGrouping<int, PlacedPlays> block) =>
        new(block.Key, block.First().Artist.BlockName!, block.Sum(QualifiedPlaysOf), ArtistCount(block));

    /// <summary>Different names can resolve to the same MusicBrainz artist: counted once.</summary>
    private static int ArtistCount(IEnumerable<PlacedPlays> plays) => plays.Select(MbidOf).Distinct().Count();

    // --- selectors ---------------------------------------------------------------------

    private static int DistrictGenreIdOf(PlacedPlays p) => p.Artist.DistrictGenreId!.Value;
    private static int BlockGenreIdOf(PlacedPlays p) => p.Artist.BlockGenreId!.Value;
    private static long QualifiedPlaysOf(PlacedPlays p) => p.Plays.QualifiedPlays;
    private static string? MbidOf(PlacedPlays p) => p.Artist.Mbid;
    private static long QualifiedPlaysOf(GenreOption g) => g.QualifiedPlays;
    private static long QualifiedPlaysOf(SubgenreOption s) => s.QualifiedPlays;
    private static string NameOf(GenreOption g) => g.Name;
    private static string NameOf(SubgenreOption s) => s.Name;
}
