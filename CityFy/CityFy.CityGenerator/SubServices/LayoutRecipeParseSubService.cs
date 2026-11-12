using System.Globalization;
using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.Models.Layout;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.CityGenerator.SubServices;

/// <summary>
/// "key=value; key=value" (spaces and case ignored). Missing keys come from the base preset
/// ("base=organic" by default). Unknown, duplicate or out-of-range keys are errors, never ignored.
/// </summary>
public sealed class LayoutRecipeParseSubService : ILayoutRecipeParseSubService
{
    public const int MaxLength = 300;
    public const string CustomName = "custom";
    private const string ProblemsExtension = "problems";
    private const int Decimals = 2;

    private const string Base = "base";
    private const string Shape = "shape";
    private const string Regularity = "regularity";
    private const string Blocks = "blocks";
    private const string Streets = "streets";
    private const string Avenues = "avenues";
    private const string Plazas = "plazas";
    private const string Heights = "heights";
    private const string Lamps = "lamps";
    private const string SeedKey = "seed";
    private const string On = "on";
    private const string Off = "off";

    private static readonly IReadOnlyDictionary<string, LayoutRecipe> Presets = new Dictionary<string, LayoutRecipe>
    {
        [CityRequest.Organic] = LayoutRecipe.Organic,
        [CityRequest.Grid] = LayoutRecipe.Grid,
    };

    private static readonly IReadOnlyDictionary<string, CityShape> Shapes = Enum.GetValues<CityShape>().ToDictionary(s => s.ToString().ToLowerInvariant());
    private static readonly IReadOnlyDictionary<string, HeightScale> HeightScales = Enum.GetValues<HeightScale>().ToDictionary(s => s.ToString().ToLowerInvariant());

    /// <summary>Canonical order.</summary>
    private static readonly RecipeKey[] Keys =
    [
        new(Base, RecipeKeyTypes.Choice, Presets.Keys.ToList(), null, null, null, true),
        new(Shape, RecipeKeyTypes.Choice, Shapes.Keys.ToList(), null, null, null, false),
        new(Regularity, RecipeKeyTypes.Number, null, 0, 1, 0.05, false),
        new(Blocks, RecipeKeyTypes.Number, null, 0.5, 2, 0.1, false),
        new(Streets, RecipeKeyTypes.Number, null, 2, 6, 0.5, false),
        new(Avenues, RecipeKeyTypes.Number, null, 4, 10, 0.5, false),
        new(Plazas, RecipeKeyTypes.Choice, [On, Off], null, null, null, false),
        new(Heights, RecipeKeyTypes.Choice, HeightScales.Keys.ToList(), null, null, null, false),
        new(Lamps, RecipeKeyTypes.Number, null, 0, 2, 0.1, false),
        new(SeedKey, RecipeKeyTypes.Integer, null, 0, 999_999, 1, true),
    ];

    private static readonly IReadOnlyDictionary<string, RecipeKey> KeysByName = Keys.ToDictionary(k => k.Key);

    public LayoutRecipe Resolve(string layout, string? recipe) => layout switch
    {
        CityRequest.Custom => Parse(recipe),
        _ when Presets.TryGetValue(layout, out var preset) => preset,
        _ => LayoutRecipe.Organic
    };

    public string Canonical(LayoutRecipe r)
    {
        var parts = new List<string>
        {
            $"{Shape}={r.Shape.ToString().ToLowerInvariant()}",
            $"{Regularity}={Format(r.Regularity)}",
            $"{Blocks}={Format(r.Blocks)}",
            $"{Streets}={Format(r.Street)}",
            $"{Avenues}={Format(r.MainRoad)}",
            $"{Plazas}={(r.Plazas ? On : Off)}",
            $"{Heights}={r.Heights.ToString().ToLowerInvariant()}",
            $"{Lamps}={Format(r.Lamps)}",
        };
        if (r.Seed is { } seed) parts.Add($"{SeedKey}={seed.ToString(CultureInfo.InvariantCulture)}");
        return string.Join("; ", parts);
    }

    public RecipeSchema Schema() =>
        new(MaxLength, Keys, Presets.ToDictionary(p => p.Key, p => Canonical(p.Value)));

    private LayoutRecipe Parse(string? text)
    {
        var problems = new List<RecipeProblem>();
        if (string.IsNullOrWhiteSpace(text))
            throw Invalid([new RecipeProblem("", RecipeProblemCodes.Missing)]);
        if (text.Length > MaxLength)
            throw Invalid([new RecipeProblem("", RecipeProblemCodes.TooLong)]);

        var values = new Dictionary<string, string>();
        foreach (var segment in text.Split(';'))
        {
            var s = segment.Trim();
            if (s.Length == 0) continue;
            var eq = s.IndexOf('=');
            if (eq <= 0)
            {
                problems.Add(new RecipeProblem(s.ToLowerInvariant(), RecipeProblemCodes.Syntax));
                continue;
            }
            var key = s[..eq].Trim().ToLowerInvariant();
            var value = s[(eq + 1)..].Trim().ToLowerInvariant();
            if (!KeysByName.ContainsKey(key)) problems.Add(new RecipeProblem(key, RecipeProblemCodes.UnknownKey));
            else if (!values.TryAdd(key, value)) problems.Add(new RecipeProblem(key, RecipeProblemCodes.DuplicateKey));
        }

        var recipe = LayoutRecipe.Organic;
        if (values.TryGetValue(Base, out var baseName))
        {
            if (Presets.TryGetValue(baseName, out var preset)) recipe = preset;
            else problems.Add(new RecipeProblem(Base, RecipeProblemCodes.InvalidValue));
        }
        recipe = recipe with { Name = CustomName };

        T? Choice<T>(string key, IReadOnlyDictionary<string, T> options) where T : struct
        {
            if (!values.TryGetValue(key, out var v)) return null;
            if (options.TryGetValue(v, out var option)) return option;
            problems.Add(new RecipeProblem(key, RecipeProblemCodes.InvalidValue));
            return null;
        }

        double? Number(string key)
        {
            if (!values.TryGetValue(key, out var v)) return null;
            var spec = KeysByName[key];
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d))
            {
                problems.Add(new RecipeProblem(key, RecipeProblemCodes.InvalidValue));
                return null;
            }
            if (d < spec.Min || d > spec.Max)
            {
                problems.Add(new RecipeProblem(key, RecipeProblemCodes.OutOfRange));
                return null;
            }
            return spec.Type == RecipeKeyTypes.Integer ? d : Math.Round(d, Decimals);
        }

        var onOff = new Dictionary<string, bool> { [On] = true, [Off] = false };
        recipe = recipe with
        {
            Shape = Choice(Shape, Shapes) ?? recipe.Shape,
            Regularity = Number(Regularity) ?? recipe.Regularity,
            Blocks = Number(Blocks) ?? recipe.Blocks,
            Street = Number(Streets) ?? recipe.Street,
            MainRoad = Number(Avenues) ?? recipe.MainRoad,
            Plazas = Choice(Plazas, onOff) ?? recipe.Plazas,
            Heights = Choice(Heights, HeightScales) ?? recipe.Heights,
            Lamps = Number(Lamps) ?? recipe.Lamps,
        };
        if (values.ContainsKey(SeedKey))
        {
            var seed = Number(SeedKey);
            if (seed is { } s && s != Math.Floor(s)) problems.Add(new RecipeProblem(SeedKey, RecipeProblemCodes.InvalidValue));
            else if (seed is { } whole) recipe = recipe with { Seed = (int)whole };
        }

        if (problems.Count > 0) throw Invalid(problems);
        return recipe;
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static CityFyException Invalid(IReadOnlyList<RecipeProblem> problems) =>
        new(ErrorCodes.CityInvalidRecipe,
            $"Invalid map parameters: {string.Join(", ", problems.Select(p => $"{(p.Key.Length > 0 ? p.Key : "(string)")} {p.Code}"))}",
            StatusCodes.Status400BadRequest)
        {
            Extensions = new Dictionary<string, object?> { [ProblemsExtension] = problems }
        };
}
