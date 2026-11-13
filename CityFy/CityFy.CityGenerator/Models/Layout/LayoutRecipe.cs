namespace CityFy.CityGenerator.Models.Layout;

/// <summary>Outline of the city.</summary>
public enum CityShape
{
    /// <summary>Irregular round outline (jittered circle).</summary>
    Circle,
    /// <summary>Rectangle 1.3 : 1.</summary>
    Rectangle,
    Square,
    /// <summary>Rectangle 2 : 1.</summary>
    Elongated
}

/// <summary>How the building height grows with the plays.</summary>
public enum HeightScale
{
    /// <summary>As computed by the catalog: logarithmic, the long tail stays visible.</summary>
    Log,
    /// <summary>Proportional to the plays: the most listened artists tower over the rest.</summary>
    Linear
}

/// <summary>
/// Parameters of a city map. "organic" and "grid" are presets; "custom" maps come from a parameter
/// string (LayoutRecipeParseSubService). Sizes in scene units.
/// </summary>
/// <param name="Regularity">Below 0.5 irregular districts (weighted Voronoi), from 0.5 rectangular districts (grid).</param>
/// <param name="Blocks">Block size multiplier: bigger blocks, fewer inner streets.</param>
/// <param name="Street">Width of the streets between blocks.</param>
/// <param name="MainRoad">Width of the main roads between districts.</param>
/// <param name="Plazas">Square with a roundabout in the middle of every irregular district.</param>
/// <param name="Lamps">Street lamp density (0 = none, 1 = one every 14 units).</param>
/// <param name="Seed">Same parameters, another seed: another arrangement. Null = the default arrangement.</param>
public sealed record LayoutRecipe(
    string Name,
    CityShape Shape,
    double Regularity,
    double Blocks,
    double Street,
    double MainRoad,
    bool Plazas,
    HeightScale Heights,
    double Lamps,
    int? Seed)
{
    /// <summary>API units -> scene units (building height 4..40 -> 1..10).</summary>
    public const double Scale = 0.25;

    /// <summary>Sidewalk ring around every block (inside the block outline).</summary>
    public const double Sidewalk = 0.7;

    /// <summary>Between the buildings of a block.</summary>
    public const double LotGap = 1;

    private const int BaseSubBlockMax = 16;
    private const int MinSubBlockMax = 4;
    private const double BaseLampSpacing = 14;

    public static readonly LayoutRecipe Organic = new("organic", CityShape.Circle, 0, 1, 3, 6, true, HeightScale.Log, 1, null);
    public static readonly LayoutRecipe Grid = new("grid", CityShape.Rectangle, 1, 1, 3, 6, false, HeightScale.Log, 1, null);

    public bool IsIrregular => Regularity < 0.5;

    /// <summary>Genre blocks with more buildings are split in sub-blocks by inner streets.</summary>
    public int SubBlockMax => Math.Max(MinSubBlockMax, (int)Math.Round(BaseSubBlockMax * Blocks));

    /// <summary>Distance between lamps along a road; infinite = no lamps.</summary>
    public double LampSpacing => Lamps > 0 ? BaseLampSpacing / Lamps : double.PositiveInfinity;

    /// <summary>Width / depth of a rectangular outline.</summary>
    public double Aspect => Shape switch
    {
        CityShape.Square => 1,
        CityShape.Elongated => 2,
        _ => 1.3
    };

    /// <summary>Text mixed into the seeds of the arrangement (empty for the default one).</summary>
    public string SeedSuffix => Seed is { } s ? $"#{s}" : "";
}
