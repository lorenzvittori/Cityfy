using CityFy.CityGenerator.Options;
using CityFy.CityGenerator.SubServices;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.CityGenerator.Tests;

public sealed class CityRequestTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CityRequestSubService Requests = new(Microsoft.Extensions.Options.Options.Create(new CityGeneratorOptions { MaxNameLength = 10, MaxGenres = 3 }));

    [Fact]
    public void Defaults_AreOrganicWithoutName_AndDatesBecomeUtc()
    {
        var r = Requests.Validate(new GenerateCityRequest(Name: "  ", From: new DateTime(2024, 1, 1), Layout: " GRID "), Now);

        Assert.Null(r.Name);
        Assert.Equal("grid", r.Layout);
        Assert.Equal(DateTimeKind.Utc, r.From!.Value.Kind);
        Assert.Equal("organic", Requests.Validate(new GenerateCityRequest(), Now).Layout);
    }

    [Theory]
    [InlineData("radial", ErrorCodes.CityInvalidLayout)]
    [InlineData("manhattan", ErrorCodes.CityInvalidLayout)]
    public void UnknownLayouts_AreRejected(string layout, string code) =>
        Assert.Equal(code, Assert.Throws<CityFyException>(() => Requests.Validate(new GenerateCityRequest(Layout: layout), Now)).Code);

    [Fact]
    public void InvalidRangesNamesAndGenres_AreRejected()
    {
        Assert.Equal(ErrorCodes.CityInvalidRange, Assert.Throws<CityFyException>(() =>
            Requests.Validate(new GenerateCityRequest(From: new DateTime(2025, 1, 1), To: new DateTime(2024, 1, 1)), Now)).Code);
        Assert.Equal(ErrorCodes.CityInvalidRange, Assert.Throws<CityFyException>(() =>
            Requests.Validate(new GenerateCityRequest(From: Now.AddDays(1)), Now)).Code);
        Assert.Equal(ErrorCodes.CityInvalidName, Assert.Throws<CityFyException>(() =>
            Requests.Validate(new GenerateCityRequest(Name: "a name that is too long"), Now)).Code);
        Assert.Equal(ErrorCodes.CityInvalidGenres, Assert.Throws<CityFyException>(() =>
            Requests.Validate(new GenerateCityRequest(Genres: [1, 2], Subgenres: [3, 4]), Now)).Code);
    }

    [Fact]
    public void Hash_IgnoresNameAndGenreOrder_ButNotTheParameters()
    {
        string Hash(GenerateCityRequest r) => Requests.Hash(Requests.Validate(r, Now));

        Assert.Equal(Hash(new(Name: "a", Genres: [2, 1])), Hash(new(Name: "b", Genres: [1, 2, 1])));
        Assert.NotEqual(Hash(new(Genres: [1])), Hash(new(Subgenres: [1])));
        Assert.NotEqual(Hash(new(Layout: "grid")), Hash(new(Layout: "organic")));
        Assert.NotEqual(Hash(new(From: new DateTime(2024, 1, 1))), Hash(new()));
    }
}
