using CityFy.CityGenerator.Models.Geometry;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Layout;

/// <summary>
/// Input of a template: districts and blocks with at least one building, size of the lots (fixed for
/// the whole city: the widest building, so none overflows its lot) and the recipe.
/// </summary>
public sealed record LayoutContext(IReadOnlyList<CityDistrict> Districts, double Pitch, double MaxWidth, double SquareRadius, LayoutRecipe Recipe)
{
    /// <summary>Bigger genre blocks are split in sub-blocks by inner streets.</summary>
    public int SubBlocks(int buildings) => (int)Math.Ceiling((double)buildings / Recipe.SubBlockMax);

    /// <summary>Ground to reserve for a block: lots, sidewalks, its share of streets, inner streets.</summary>
    public double BlockArea(int buildings)
    {
        var k = SubBlocks(buildings);
        var lots = Math.Sqrt(buildings * Pitch * Pitch * 1.15) + MaxWidth * 0.5;
        var side = lots + 2 * LayoutRecipe.Sidewalk * Math.Sqrt(k) + Recipe.Street;
        var inner = 2 * (Math.Sqrt(k) - 1) * Recipe.Street * side;
        return side * side + inner;
    }

    public double DistrictArea(CityDistrict district)
    {
        var sum = 0.0;
        foreach (var b in district.Blocks) sum += BlockArea(b.Buildings.Count);
        return sum;
    }
}

/// <summary>A block drawn by a template: Cell up to the center line of the surrounding streets; Bound = buildable part of the district (main roads excluded), convex.</summary>
public sealed record TemplateBlock(CityBlock Source, string DistrictId, string Color, Polygon Cell, Polygon Bound);

/// <summary>Avenues: roads that are not the border between two cells.</summary>
public sealed record TemplateResult(List<PlacedDistrict> Districts, List<TemplateBlock> Blocks, List<Plaza> Plazas, List<RoadSegment> Avenues);
