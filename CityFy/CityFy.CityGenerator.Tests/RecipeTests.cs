using System.Text.Json;
using CityFy.CityGenerator.Models.Layout;
using CityFy.CityGenerator.SubServices;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.CityGenerator.Tests;

public sealed class RecipeTests
{
    private static readonly LayoutRecipeParseSubService Recipes = new();
    private static readonly CityLayoutSubService Layouts = new();

    private static List<CityDistrict> Sample(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", $"{name}-organic.json"))).RootElement
            .GetProperty("districts").Deserialize<List<CityDistrict>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static IReadOnlyList<RecipeProblem> ProblemsOf(string text)
    {
        var e = Assert.Throws<CityFyException>(() => Recipes.Resolve("custom", text));
        Assert.Equal(ErrorCodes.CityInvalidRecipe, e.Code);
        return (IReadOnlyList<RecipeProblem>)e.Extensions["problems"]!;
    }

    [Fact]
    public void MissingKeys_ComeFromTheBase_AndTheCanonicalStringHasEveryKey()
    {
        var r = Recipes.Resolve("custom", " BASE=grid ; blocks=0.6;plazas=on ");

        Assert.Equal(("custom", CityShape.Rectangle, 1.0, 0.6, true), (r.Name, r.Shape, r.Regularity, r.Blocks, r.Plazas));
        Assert.Equal("shape=rectangle; regularity=1; blocks=0.6; streets=3; avenues=6; plazas=on; heights=log; lamps=1", Recipes.Canonical(r));
    }

    [Fact]
    public void EqualParameters_GiveTheSameCanonicalString()
    {
        Assert.Equal(
            Recipes.Canonical(Recipes.Resolve("custom", "regularity=0.70; lamps=1.5")),
            Recipes.Canonical(Recipes.Resolve("custom", "lamps=1.50;regularity=0.7;base=organic")));
    }

    [Fact]
    public void TheCanonicalStringOfAPreset_GivesThePresetBack()
    {
        foreach (var (name, preset) in new[] { ("organic", LayoutRecipe.Organic), ("grid", LayoutRecipe.Grid) })
        {
            var back = Recipes.Resolve("custom", Recipes.Schema().Bases[name]);
            Assert.Equal(preset with { Name = "custom" }, back);
        }
    }

    [Fact]
    public void Errors_AreReportedPerKey_NeverIgnored()
    {
        var problems = ProblemsOf("regularity=3; colour=red; plazas=on; plazas=off; shape=triangle; blocks=many; nonsense; seed=1.5");

        Assert.Equal(
        [
            new RecipeProblem("colour", RecipeProblemCodes.UnknownKey),
            new RecipeProblem("plazas", RecipeProblemCodes.DuplicateKey),
            new RecipeProblem("nonsense", RecipeProblemCodes.Syntax),
            new RecipeProblem("shape", RecipeProblemCodes.InvalidValue),
            new RecipeProblem("regularity", RecipeProblemCodes.OutOfRange),
            new RecipeProblem("blocks", RecipeProblemCodes.InvalidValue),
            new RecipeProblem("seed", RecipeProblemCodes.InvalidValue),
        ], problems.OrderBy(p => p.Code == RecipeProblemCodes.UnknownKey ? 0 : p.Code == RecipeProblemCodes.DuplicateKey ? 1 : p.Code == RecipeProblemCodes.Syntax ? 2 : 3));
        Assert.Equal([new RecipeProblem("", RecipeProblemCodes.Missing)], ProblemsOf("  "));
        Assert.Equal([new RecipeProblem("", RecipeProblemCodes.TooLong)], ProblemsOf(new string('x', 301)));
    }

    [Fact]
    public void Schema_ListsTheKeysInCanonicalOrder_WithThePresets()
    {
        var schema = Recipes.Schema();

        Assert.Equal(["base", "shape", "regularity", "blocks", "streets", "avenues", "plazas", "heights", "lamps", "seed"], schema.Keys.Select(k => k.Key));
        Assert.Equal(["organic", "grid"], schema.Bases.Keys);
        Assert.Equal((0.5, 2.0), (schema.Keys.Single(k => k.Key == "blocks").Min, schema.Keys.Single(k => k.Key == "blocks").Max));
    }

    [Theory]
    [InlineData("shape=square")]
    [InlineData("shape=elongated; regularity=0.2")]
    [InlineData("base=grid; shape=circle; heights=linear; seed=7")]
    [InlineData("base=grid; blocks=0.5; streets=2; avenues=10; lamps=2")]
    [InlineData("regularity=0; plazas=off; blocks=2; lamps=0")]
    public void CustomMaps_PlaceEveryArtistOnce(string text)
    {
        var districts = Sample("medium");

        var map = Layouts.Compute(new CityModel { Districts = districts }, Recipes.Resolve("custom", text));

        Assert.Equal("custom", map.Template);
        Assert.Equal(districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings).Select(b => b.BuildingId).Order(), map.Buildings.Select(b => b.ArtistId).Order());
        Assert.All(map.Buildings, b => Assert.True(double.IsFinite(b.X) && double.IsFinite(b.Z)));
    }

    [Fact]
    public void Parameters_ChangeTheMap()
    {
        var districts = Sample("medium");
        // plays growing with the (log) height, as the catalog computes them
        foreach (var b in districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings)) b.QualifiedPlays = (long)Math.Pow(2, b.Height / 4);
        var city = new CityModel { Districts = districts };
        CityLayout Map(string text) => Layouts.Compute(city, Recipes.Resolve("custom", text));

        Assert.Empty(Map("lamps=0").Streets.Lamps);
        Assert.True(Map("lamps=2").Streets.Lamps.Count > Map("lamps=0.5").Streets.Lamps.Count);
        Assert.NotEqual(Map("seed=1").Buildings[0].X, Map("seed=2").Buildings[0].X);
        Assert.Equal(Map("base=organic").Buildings.Count, Map("seed=1").Buildings.Count);
        Assert.True(Map("blocks=0.5").Blocks.Sum(b => b.Parts.Count) > Map("blocks=2").Blocks.Sum(b => b.Parts.Count));
        Assert.Empty(Map("plazas=off").Plazas);

        // Linear heights: same tallest building, the others lower than on the log scale
        var log = Map("heights=log").Buildings;
        var linear = Map("heights=linear").Buildings;
        Assert.Equal(log.Max(b => b.Height), linear.Max(b => b.Height), 6);
        Assert.True(linear.Average(b => b.Height) < log.Average(b => b.Height));
    }

    [Fact]
    public void SameParameters_GiveTheSameMap()
    {
        var city = new CityModel { Districts = Sample("small") };
        var recipe = Recipes.Resolve("custom", "shape=elongated; seed=42");

        Assert.Equal(JsonSerializer.Serialize(Layouts.Compute(city, recipe)), JsonSerializer.Serialize(Layouts.Compute(city, recipe)));
    }
}
