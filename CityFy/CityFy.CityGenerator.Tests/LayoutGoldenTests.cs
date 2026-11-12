using System.Text.Json;
using CityFy.CityGenerator.SubServices;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Tests;

/// <summary>
/// The C# layout must reproduce the map the frontend used to compute. Golden/*.json is the frozen
/// output of the former TypeScript layout on sample cities (layout + road markings), exported
/// before it was removed from cityfy-web: a change of the map algorithm must update them on purpose.
/// </summary>
public sealed class LayoutGoldenTests
{
    private static readonly LayoutRecipeParseSubService Recipes = new();
    private const double Tolerance = 1e-6;
    private const int MaxReported = 15;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Fixtures() =>
        new(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden"), "*.json").Select(Path.GetFileNameWithoutExtension).Order()!);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Layout_MatchesTheTypeScriptLayout(string fixture)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", fixture + ".json")));
        var root = doc.RootElement;
        var districts = root.GetProperty("districts").Deserialize<List<CityDistrict>>(Json)!;
        var layouts = new CityLayoutSubService();

        var layout = layouts.Compute(new CityModel { Districts = districts }, Recipes.Resolve(root.GetProperty("template").GetString()!, null));
        var actual = JsonSerializer.SerializeToElement(layout, Json);

        var errors = new List<string>();
        Compare(root.GetProperty("layout"), actual, "layout", errors);
        Compare(root.GetProperty("markings"), actual.GetProperty("streets").GetProperty("markings"), "markings", errors);
        Assert.True(errors.Count == 0, $"{fixture}: {errors.Count} differences\n{string.Join('\n', errors.Take(MaxReported))}");
    }

    [Fact]
    public void Layout_IsDeterministic()
    {
        var file = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden"), "medium-organic.json").Single();
        var districts = JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("districts").Deserialize<List<CityDistrict>>(Json)!;
        var layouts = new CityLayoutSubService();

        var first = JsonSerializer.Serialize(layouts.Compute(new CityModel { Districts = districts }, Recipes.Resolve("organic", null)), Json);
        var second = JsonSerializer.Serialize(layouts.Compute(new CityModel { Districts = districts }, Recipes.Resolve("organic", null)), Json);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EveryArtist_IsPlacedExactlyOnce()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Golden"), "*.json"))
        {
            var root = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
            var districts = root.GetProperty("districts").Deserialize<List<CityDistrict>>(Json)!;
            var layouts = new CityLayoutSubService();
            var layout = layouts.Compute(new CityModel { Districts = districts }, Recipes.Resolve(root.GetProperty("template").GetString()!, null));

            var expected = districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings).Select(b => b.BuildingId).Order();
            Assert.Equal(expected, layout.Buildings.Select(b => b.ArtistId).Order());
        }
    }

    [Fact]
    public void EmptyCity_HasAnEmptyMap()
    {
        var layouts = new CityLayoutSubService();

        var layout = layouts.Compute(new CityModel(), Recipes.Resolve("grid", null));

        Assert.Equal(("grid", 0, 0.0), (layout.Template, layout.Buildings.Count, layout.Width));
    }

    /// <summary>Every property of expected must be in actual (actual may have more), numbers within the tolerance.</summary>
    private static void Compare(JsonElement expected, JsonElement actual, string path, List<string> errors)
    {
        if (errors.Count > MaxReported) return;
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                if (actual.ValueKind != JsonValueKind.Object) { errors.Add($"{path}: expected an object, got {actual.ValueKind}"); return; }
                foreach (var p in expected.EnumerateObject())
                {
                    if (actual.TryGetProperty(p.Name, out var a)) Compare(p.Value, a, $"{path}.{p.Name}", errors);
                    else errors.Add($"{path}.{p.Name}: missing");
                }
                break;
            case JsonValueKind.Array:
                if (actual.ValueKind != JsonValueKind.Array) { errors.Add($"{path}: expected an array, got {actual.ValueKind}"); return; }
                if (expected.GetArrayLength() != actual.GetArrayLength())
                {
                    errors.Add($"{path}: length {actual.GetArrayLength()}, expected {expected.GetArrayLength()}");
                    return;
                }
                var i = 0;
                foreach (var (e, a) in expected.EnumerateArray().Zip(actual.EnumerateArray()))
                    Compare(e, a, $"{path}[{i++}]", errors);
                break;
            case JsonValueKind.Number:
                if (actual.ValueKind != JsonValueKind.Number) { errors.Add($"{path}: expected a number, got {actual.ValueKind}"); return; }
                var (ev, av) = (expected.GetDouble(), actual.GetDouble());
                if (Math.Abs(ev - av) > Tolerance * Math.Max(1, Math.Abs(ev))) errors.Add($"{path}: {av}, expected {ev}");
                break;
            default:
                if (expected.ToString() != actual.ToString()) errors.Add($"{path}: '{actual}', expected '{expected}'");
                break;
        }
    }
}
