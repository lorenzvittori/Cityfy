using ServiceDefault.City;
using CityFy.Catalog.Models;
using CityFy.Catalog.Options;
using CityFy.Catalog.SubServices;

namespace CityFy.Catalog.Tests;

public sealed class CityBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CityOptions Options = new();

    private static ResolvedArtist Artist(string name, string mbid, params (int Id, string Name, int Votes)[] genres) =>
        new(name, "name", 1, mbid, name, genres.Select(g => new ResolvedGenre(g.Id, g.Name, g.Votes)).ToList());

    private static ArtistPlays Plays(string name, long qualified, long ms = 0, string? uri = null) =>
        new() { ArtistName = name, Plays = qualified, QualifiedPlays = qualified, MsPlayed = ms, ArtistUri = uri };

    // Placement (catalog refresh) then build, as the pipeline does
    internal static CityModel Build(
        IReadOnlyList<ArtistPlays> plays, Dictionary<string, ResolvedArtist> resolved, CityOptions? options = null, GenreFilter? filter = null,
        DistrictLimit? limit = null)
    {
        var o = Microsoft.Extensions.Options.Options.Create(options ?? Options);
        var placements = new ArtistPlacementSubService(o)
            .Place("user1", "v1", plays, resolved, new GenreHierarchy(GenreHierarchyTests.SampleGraph()))
            .ToDictionary(p => p.ArtistName);
        var builder = new CityBuildSubService(o);
        return builder.Build("user1", null, null, Now, plays, placements, filter ?? GenreFilter.All, limit ?? builder.Limit(null, null));
    }

    // Three macro-genres: rock (30), electronic (20), genre 99 (10: without parents, its own district)
    private static readonly ArtistPlays[] ThreeDistricts = [Plays("R", 30), Plays("E", 20), Plays("J", 10)];
    private static readonly Dictionary<string, ResolvedArtist> ThreeDistrictsResolved = new()
    {
        ["R"] = Artist("R", "mb-r", (1, "rock", 1)),
        ["E"] = Artist("E", "mb-e", (4, "electronic", 1)),
        ["J"] = Artist("J", "mb-j", (99, "jazz", 1)),
    };

    [Fact]
    public void Artist_IsPlacedInTheDistrictOfItsGenreRoot_AndTheBlockOfItsGenre()
    {
        var city = Build(
            [Plays("Nirvana", 10)],
            new() { ["Nirvana"] = Artist("Nirvana", "mb-nirvana", (3, "grunge", 5)) });

        var district = Assert.Single(city.Districts);
        Assert.Equal("rock", district.Name);
        var block = Assert.Single(district.Blocks);
        Assert.Equal("grunge", block.Name);
        Assert.Equal("rock__grunge", block.BlockId);
        Assert.Equal("Nirvana", Assert.Single(block.Buildings).Name);
    }

    [Fact]
    public void Macro_IsTheRootWithMostGenreVotes()
    {
        // 1 vote for grunge (rock) vs 4 for house (electronic) -> electronic district, house block
        var city = Build(
            [Plays("X", 3)],
            new() { ["X"] = Artist("X", "mb-x", (5, "house", 4), (3, "grunge", 1)) });

        var district = Assert.Single(city.Districts);
        Assert.Equal("electronic", district.Name);
        Assert.Equal("house", Assert.Single(district.Blocks).Name);
    }

    [Fact]
    public void UnresolvedArtists_AreNotPlaced_AndAreReported()
    {
        var city = Build(
            [Plays("Known", 10), Plays("Unknown", 7)],
            new() { ["Known"] = Artist("Known", "mb-known", (1, "rock", 1)) });

        Assert.Equal(1, city.Summary.PlacedArtists);
        Assert.Equal(2, city.Summary.TotalArtists);
        Assert.Equal("Unknown", Assert.Single(city.Unresolved).Name);
        Assert.Equal(Math.Round(100.0 * 10 / 17, 2), city.Summary.CoveragePercent);
    }

    [Fact]
    public void NamesResolvingToTheSameArtist_BecomeOneBuilding()
    {
        var artist = Artist("Sigur Rós", "mb-sigur", (1, "rock", 2));
        var city = Build(
            [Plays("Sigur Rós", 5), Plays("Sigur Ros", 3)],
            new() { ["Sigur Rós"] = artist, ["Sigur Ros"] = artist });

        var building = Assert.Single(city.Districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings));
        Assert.Equal(8, building.QualifiedPlays);
    }

    [Fact]
    public void Heights_AreBetweenMinAndMax_AndTheMostPlayedArtistIsTallest()
    {
        var city = Build(
            [Plays("A", 100), Plays("B", 1)],
            new()
            {
                ["A"] = Artist("A", "mb-a", (1, "rock", 1)),
                ["B"] = Artist("B", "mb-b", (1, "rock", 1)),
            });

        var buildings = city.Districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings).ToDictionary(b => b.Name);
        Assert.Equal(Options.MaxHeight, buildings["A"].Height);
        Assert.InRange(buildings["B"].Height, Options.MinHeight, Options.MaxHeight);
        Assert.True(buildings["A"].Height > buildings["B"].Height);
    }

    [Fact]
    public void Districts_AreOrderedByScore_WithIntensitySummingTo100()
    {
        var city = Build(
            [Plays("R", 30), Plays("E", 10)],
            new()
            {
                ["R"] = Artist("R", "mb-r", (1, "rock", 1)),
                ["E"] = Artist("E", "mb-e", (4, "electronic", 1)),
            });

        Assert.Equal(["rock", "electronic"], city.Districts.Select(d => d.Name));
        Assert.Equal(100, city.Districts.Sum(d => d.ListeningIntensity), 1);
    }

    [Fact]
    public void ArtistsWithoutQualifiedPlays_AreIgnored()
    {
        var city = Build(
            [new ArtistPlays { ArtistName = "Skipped", Plays = 4, QualifiedPlays = 0 }],
            new() { ["Skipped"] = Artist("Skipped", "mb-s", (1, "rock", 1)) });

        Assert.Empty(city.Districts);
        Assert.Empty(city.Unresolved);
    }

    [Fact]
    public void Warning_NoPlays_WhenNothingWasListened()
    {
        var city = Build([], new());

        Assert.Equal([ServiceDefault.Errors.ErrorCodes.CityNoPlays], city.Warnings);
    }

    [Fact]
    public void Warning_GenresUnavailable_WhenNoArtistIsResolved()
    {
        var city = Build([Plays("Unknown", 5)], new());

        Assert.Equal([ServiceDefault.Errors.ErrorCodes.CityGenresUnavailable], city.Warnings);
    }

    [Fact]
    public void Warning_LowCoverage_WhenLessThanHalfIsPlaced()
    {
        var city = Build(
            [Plays("Known", 4), Plays("Unknown", 6)],
            new() { ["Known"] = Artist("Known", "mb-known", (1, "rock", 1)) });

        Assert.Equal([ServiceDefault.Errors.ErrorCodes.CityLowCoverage], city.Warnings);
    }

    [Fact]
    public void NoWarnings_WhenMostPlaysArePlaced()
    {
        var city = Build(
            [Plays("Known", 9), Plays("Unknown", 1)],
            new() { ["Known"] = Artist("Known", "mb-known", (1, "rock", 1)) });

        Assert.Empty(city.Warnings);
    }

    [Fact]
    public void BeyondTheLimit_Merge_TheLeastListenedBecomeOtherGenres()
    {
        var city = Build(ThreeDistricts, ThreeDistrictsResolved, limit: new DistrictLimit(2, ExtraGenres.Merge));

        // 3 districts, max 2: rock (30) stays, electronic (20) and genre 99 (10) are merged
        Assert.Equal(["rock", CityBuildSubService.OtherDistrictId], city.Districts.Select(d => d.DistrictId));
        var other = city.Districts[1];
        Assert.Equal("Other genres", other.Name);
        Assert.Equal(2, other.Blocks.Count);
        Assert.Equal(30, other.Score);
        Assert.Equal(100, city.Districts.Sum(d => d.ListeningIntensity), 1);
        Assert.Equal(60, city.Summary.QualifiedPlays);
    }

    [Fact]
    public void BeyondTheLimit_Drop_TheLeastListenedLeaveTheCityAndItsTotals()
    {
        var city = Build([.. ThreeDistricts, Plays("Unknown", 5)], ThreeDistrictsResolved, limit: new DistrictLimit(2, ExtraGenres.Drop));

        Assert.Equal(["rock", "electronic"], city.Districts.Select(d => d.DistrictId));
        // genre 99 is left out as the genre filter would: the unresolved artist still counts
        Assert.Equal(55, city.Summary.QualifiedPlays);
        Assert.Equal(3, city.Summary.TotalArtists);
        Assert.Equal(Math.Round(100.0 * 50 / 55, 2), city.Summary.CoveragePercent);
        Assert.Equal("Unknown", Assert.Single(city.Unresolved).Name);
        Assert.Empty(city.Warnings);
        Assert.Equal(100, city.Districts.Sum(d => d.ListeningIntensity), 1);
    }

    [Theory]
    [InlineData(ExtraGenres.Merge)]
    [InlineData(ExtraGenres.Drop)]
    public void LimitAll_KeepsEveryMacroGenre(ExtraGenres extra)
    {
        var city = Build(ThreeDistricts, ThreeDistrictsResolved, limit: new DistrictLimit(DistrictLimit.All, extra));

        // genre 99 is not in the sample graph: its district is named after its id
        Assert.Equal(["rock", "electronic", "genre_99"], city.Districts.Select(d => d.DistrictId));
    }

    [Fact]
    public void WithinTheLimit_NothingIsMergedOrDropped()
    {
        var merged = Build(ThreeDistricts, ThreeDistrictsResolved, limit: new DistrictLimit(3, ExtraGenres.Merge));
        var dropped = Build(ThreeDistricts, ThreeDistrictsResolved, limit: new DistrictLimit(3, ExtraGenres.Drop));

        Assert.Equal(3, merged.Districts.Count);
        Assert.Equal(3, dropped.Districts.Count);
    }

    [Fact]
    public void Warning_ManyDistricts_BeyondTheThreshold()
    {
        var city = Build(ThreeDistricts, ThreeDistrictsResolved, new CityOptions { WarnDistricts = 2 });

        Assert.Equal([ServiceDefault.Errors.ErrorCodes.CityManyDistricts], city.Warnings);
    }

    [Fact]
    public void Limit_DefaultsToTheConfiguredNumberAndMerge()
    {
        var builder = new CityBuildSubService(Microsoft.Extensions.Options.Options.Create(new CityOptions { DefaultDistricts = 7 }));

        Assert.Equal(new DistrictLimit(7, ExtraGenres.Merge), builder.Limit(null, null));
        Assert.Equal(new DistrictLimit(1, ExtraGenres.Drop), builder.Limit(1, " DROP "));
        Assert.Equal(new DistrictLimit(0, ExtraGenres.Merge), builder.Limit(0, "merge"));
    }

    [Theory]
    [InlineData(1, "merge")]  // merge needs one real genre beside "Other genres"
    [InlineData(-1, "drop")]
    [InlineData(5, "other")]
    public void Limit_Invalid_IsRejected(int max, string extra)
    {
        var builder = new CityBuildSubService(Microsoft.Extensions.Options.Options.Create(Options));

        var e = Assert.Throws<ServiceDefault.Errors.CityFyException>(() => builder.Limit(max, extra));
        Assert.Equal(ServiceDefault.Errors.ErrorCodes.CityInvalidDistricts, e.Code);
        Assert.Equal(400, e.StatusCode);
    }

    [Fact]
    public void SpotifyLink_ComesFromThePlays_ElseFromMusicBrainz()
    {
        var city = Build(
            [Plays("Api", 5, uri: "spotify:artist:fromPlays"), Plays("Export", 3)],
            new()
            {
                ["Api"] = Artist("Api", "mb-api", (1, "rock", 1)) with { SpotifyArtistId = "fromMusicBrainz1" },
                ["Export"] = Artist("Export", "mb-export", (1, "rock", 1)) with { SpotifyArtistId = "fromMusicBrainz2" },
            });

        var buildings = city.Districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings).ToDictionary(b => b.Name);
        Assert.Equal("spotify:artist:fromPlays", buildings["Api"].SpotifyArtistUri);       // the export has no artist ids:
        Assert.Equal("spotify:artist:fromMusicBrainz2", buildings["Export"].SpotifyArtistUri); // MusicBrainz link
    }

    [Fact]
    public void DistrictColor_IsStableAcrossBuilds()
    {
        var resolved = new Dictionary<string, ResolvedArtist> { ["R"] = Artist("R", "mb-r", (1, "rock", 1)) };

        var first = Build([Plays("R", 1)], resolved).Districts[0].Color;
        var second = Build([Plays("R", 1)], resolved).Districts[0].Color;

        Assert.Equal(first, second);
        Assert.Matches("^#[0-9a-f]{6}$", first);
    }
}
