using CityFy.CityGenerator.Models.Geometry;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Layout;

/// <summary>
/// "Grid": a rectangular city of straight streets, like Manhattan. The city rectangle is split into
/// districts with areas proportional to their buildings (treemap by recursive halving, cuts across
/// the longest side), each district into its blocks the same way. Main roads between districts,
/// streets between blocks: no empty ground.
/// </summary>
public sealed class GridTemplate(LayoutContext ctx)
{
    private const int CircleSides = 32;

    public static readonly CityTemplate Template = ctx => new GridTemplate(ctx).Build();

    public TemplateResult Build()
    {
        var mainRoad = ctx.Recipe.MainRoad;

        // With a seed the districts are laid out in another (deterministic) order
        var districts = ctx.Recipe.Seed is null
            ? ctx.Districts
            : ctx.Districts.OrderBy(d => new Seed(d.DistrictId + ctx.Recipe.SeedSuffix).Unit()).ToList();

        // District areas with their ring of main road
        var areas = districts.Select(d => Square(Math.Sqrt(ctx.DistrictArea(d)) + mainRoad)).ToList();
        var total = 0.0;
        foreach (var a in areas) total += a;
        var city = ctx.Recipe.Shape == CityShape.Circle ? Polygon.Regular(total, CircleSides) : Polygon.Rectangle(total, ctx.Recipe.Aspect);

        var result = new TemplateResult([], [], [], []);
        var pieces = city.SplitByAreas(areas, Polygon.AxisCuts);

        for (var di = 0; di < districts.Count; di++)
        {
            var d = districts[di];
            var outline = pieces[di];
            var c = outline.Centroid;
            result.Districts.Add(new PlacedDistrict(d.DistrictId, d.Name, d.Color, c.X, c.Z,
                Math.Sqrt(outline.Area / Math.PI), [outline.Points]));

            // Block cells up to the street center lines, buildable inside the main roads
            var inner = outline.Inset(mainRoad / 2);
            var bound = inner.IsValid ? inner : outline;
            var cells = outline.SplitByAreas(d.Blocks.Select(b => ctx.BlockArea(b.Buildings.Count)).ToList(), Polygon.AxisCuts);
            for (var bi = 0; bi < d.Blocks.Count; bi++)
                result.Blocks.Add(new TemplateBlock(d.Blocks[bi], d.DistrictId, d.Color, cells[bi], bound));
        }

        return result;
    }

    private static double Square(double x) => x * x;
}
