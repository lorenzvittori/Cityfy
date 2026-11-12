using ServiceDefault.City;
using CityFy.Elaboration.Models;
using CityFy.Elaboration.Options;
using CityFy.Elaboration.SubServices;

namespace CityFy.Elaboration.Tests;

public sealed class CityBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CityOptions Options = new();

    private static ResolvedArtist Artist(string name, string mbid, params (int Id, string Name, int Votes)[] genres) =>
        new(name, "name", 1, mbid, name, genres.Select(g => new ResolvedGenre(g.Id, g.Name, g.Votes)).ToList());

    private static ArtistPlays Plays(string name, long qualified, long ms = 0, string? uri = null) =>
        new() { ArtistName = name, Plays = qualified, QualifiedPlays = qualified, MsPlayed = ms, ArtistUri = uri };

    private static CityModel Build(IReadOnlyList<ArtistPlays> plays, Dictionary<string, ResolvedArtist> resolved) =>
        new CityBuildSubService(Microsoft.Extensions.Options.Options.Create(Options)).Build("user1", "all", null, Now, plays, resolved,
            new GenreHierarchy(GenreHierarchyTests.SampleGraph()));

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
    public void BeyondMaxDistricts_TheLeastListenedAreMergedIntoOtherGenres()
    {
        var options = new CityOptions { MaxDistricts = 2 };
        var city = new CityBuildSubService(Microsoft.Extensions.Options.Options.Create(options)).Build("user1", "all", null, Now,
            [Plays("R", 30), Plays("E", 20), Plays("J", 10)],
            new Dictionary<string, ResolvedArtist>
            {
                ["R"] = Artist("R", "mb-r", (1, "rock", 1)),
                ["E"] = Artist("E", "mb-e", (4, "electronic", 1)),
                ["J"] = Artist("J", "mb-j", (99, "jazz", 1)), // genre without parents: its own district
            },
            new GenreHierarchy(GenreHierarchyTests.SampleGraph()));

        // 3 districts, max 2: rock (30) stays, electronic (20) and jazz (10) are merged
        Assert.Equal(["rock", CityBuildSubService.OtherDistrictId], city.Districts.Select(d => d.DistrictId));
        var other = city.Districts[1];
        Assert.Equal("Other genres", other.Name);
        Assert.Equal(2, other.Blocks.Count);
        Assert.Equal(30, other.Score);
        Assert.Equal(100, city.Districts.Sum(d => d.ListeningIntensity), 1);
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
