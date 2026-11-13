using CityFy.Catalog.Models;
using CityFy.Catalog.Options;
using CityFy.Catalog.SubServices;
using ServiceDefault.Errors;

namespace CityFy.Catalog.Tests;

public sealed class UserCatalogTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private static ResolvedArtist Artist(string name, string mbid, params (int Id, string Name, int Votes)[] genres) =>
        new(name, "name", 1, mbid, name, genres.Select(g => new ResolvedGenre(g.Id, g.Name, g.Votes)).ToList());

    private static ArtistPlays Plays(string name, long qualified) =>
        new() { ArtistName = name, Plays = qualified, QualifiedPlays = qualified };

    // rock (1) > grunge (3); electronic (4) > house (5)
    private static readonly ArtistPlays[] SamplePlays = [Plays("Nirvana", 10), Plays("Foo", 6), Plays("Daft", 4), Plays("Nobody", 3)];

    private static readonly Dictionary<string, ResolvedArtist> SampleResolved = new()
    {
        ["Nirvana"] = Artist("Nirvana", "mb-nirvana", (3, "grunge", 5)),
        ["Foo"] = Artist("Foo", "mb-foo", (1, "rock", 2)),
        ["Daft"] = Artist("Daft", "mb-daft", (5, "house", 3)),
    };

    private static IReadOnlyList<ArtistPlacement> Place() =>
        new ArtistPlacementSubService(Microsoft.Extensions.Options.Options.Create(new CityOptions()))
            .Place("user1", "v1", SamplePlays, SampleResolved, new GenreHierarchy(GenreHierarchyTests.SampleGraph()));

    [Fact]
    public void Placement_HasDistrictAndBlock_AndUnmatchedArtistsAreStoredUnplaced()
    {
        var placements = Place().ToDictionary(p => p.ArtistName);

        Assert.Equal(4, placements.Count);
        Assert.Equal((1, "rock", 3, "grunge"), (placements["Nirvana"].DistrictGenreId!.Value, placements["Nirvana"].DistrictName,
            placements["Nirvana"].BlockGenreId!.Value, placements["Nirvana"].BlockName));
        Assert.Equal("user1|v1|Nirvana", placements["Nirvana"].Id);
        Assert.False(placements["Nobody"].IsPlaced);
        Assert.Matches("^#[0-9a-f]{6}$", placements["Daft"].DistrictColor);
    }

    [Fact]
    public void FilterOnGenre_KeepsTheWholeDistrict_AndTheOthersDisappear()
    {
        var city = CityBuilderTests.Build(SamplePlays, SampleResolved, filter: new GenreFilter([1], []));

        var district = Assert.Single(city.Districts);
        Assert.Equal("rock", district.Name);
        Assert.Equal(2, district.Blocks.Count);                 // grunge + rock
        Assert.Empty(city.Unresolved);                          // unmatched artists are not reported with a filter
        Assert.Equal(2, city.Summary.TotalArtists);             // totals only over the chosen genres
        Assert.Equal(16, city.Summary.QualifiedPlays);
        Assert.Equal(100, city.Summary.CoveragePercent);
    }

    [Fact]
    public void FilterOnSubgenre_KeepsOnlyThatBlock_AndCombinesWithGenres()
    {
        var onlyGrunge = CityBuilderTests.Build(SamplePlays, SampleResolved, filter: new GenreFilter([], [3]));
        Assert.Equal("grunge", Assert.Single(Assert.Single(onlyGrunge.Districts).Blocks).Name);

        var grungeAndElectronic = CityBuilderTests.Build(SamplePlays, SampleResolved, filter: new GenreFilter([4], [3]));
        Assert.Equal(["rock", "electronic"], grungeAndElectronic.Districts.Select(d => d.Name));
    }

    [Fact]
    public void FilterMatchingNothing_GivesAnEmptyCityWithNoPlaysWarning()
    {
        var city = CityBuilderTests.Build(SamplePlays, SampleResolved, filter: new GenreFilter([999], []));

        Assert.Empty(city.Districts);
        Assert.Equal([ErrorCodes.CityNoPlays], city.Warnings);
    }

    [Fact]
    public void GenreOptions_ListDistrictsWithSubgenres_MostListenedFirst()
    {
        var options = new GenreOptionsSubService().List(SamplePlays, Place().ToDictionary(p => p.ArtistName));

        Assert.Equal(["rock", "electronic"], options.Select(o => o.Name));
        var rock = options[0];
        Assert.Equal((16L, 2), (rock.QualifiedPlays, rock.Artists));
        Assert.Equal(["grunge", "rock"], rock.Subgenres.Select(s => s.Name));
    }

    [Fact]
    public void DateRange_IsUtc_AndRejectsInvertedOrFutureRanges()
    {
        var ranges = new DateRangeSubService();

        var (from, to) = ranges.Validate(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1), Now);
        Assert.Equal(DateTimeKind.Utc, from!.Value.Kind);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal((null, null), ranges.Validate(null, null, Now));

        Assert.Equal(ErrorCodes.CityInvalidRange,
            Assert.Throws<CityFyException>(() => ranges.Validate(new DateTime(2025, 1, 1), new DateTime(2024, 1, 1), Now)).Code);
        Assert.Equal(ErrorCodes.CityInvalidRange,
            Assert.Throws<CityFyException>(() => ranges.Validate(Now.AddDays(1), null, Now)).Code);
    }
}
