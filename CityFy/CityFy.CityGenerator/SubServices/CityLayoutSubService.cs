using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.Models.Geometry;
using CityFy.CityGenerator.Models.Layout;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

/// <summary>
/// City map, in four steps (port of the frontend layout, checked by golden tests):
///   1. a template (chosen by the recipe) draws districts and genre blocks as convex cells up to the road center lines;
///   2. big genre blocks are split in sub-blocks by inner streets; every sub-block is shrunk by half
///      a street (main roads: by the district bound) and gets a sidewalk ring around its plate;
///   3. artists get a lot on the plates: grid aligned with the longest side, most listened in the middle;
///   4. roads are the borders shared by cells: markings, crossings, lamps, lights and signs along them.
/// </summary>
public sealed class CityLayoutSubService : ICityLayoutSubService
{
    private const double MinLotWidth = 1;
    private const double MinSquareRadius = 3.5;
    private const double SquarePerPitch = 1.8;
    private const double MinHeight = 0.2;


    public CityLayout Compute(CityModel city, LayoutRecipe recipe)
    {
        // Only blocks with buildings, only districts with blocks
        var districts = city.Districts
            .Select(d => new CityDistrict
            {
                DistrictId = d.DistrictId, Name = d.Name, GenreId = d.GenreId, Color = d.Color,
                Blocks = d.Blocks.Where(b => b.Buildings.Count > 0).ToList()
            })
            .Where(d => d.Blocks.Count > 0)
            .ToList();
        if (districts.Count == 0)
            return new CityLayout(recipe.Name, [], [], [], [], new Streets([], [], [], [], [], []), new LayoutBounds(0, 0, 0, 0), 0, 0);

        // Lot size is fixed for the whole city (widest building), so no building overflows its lot
        var widest = MinLotWidth;
        foreach (var b in districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings)) widest = Math.Max(widest, b.Width);
        var maxWidth = widest * LayoutRecipe.Scale;
        var pitch = maxWidth + LayoutRecipe.LotGap;
        var ctx = new LayoutContext(districts, pitch, maxWidth, Math.Max(MinSquareRadius, pitch * SquarePerPitch), recipe);

        CityTemplate template = recipe.IsIrregular ? OrganicTemplate.Template : GridTemplate.Template;
        var heightOf = HeightScaleOf(districts, recipe.Heights);
        var shape = template(ctx);

        // Sub-blocks, sidewalks and buildings
        var buildings = new List<PlacedBuilding>();
        var blocks = new List<PlacedBlock>();
        var allParts = new List<(Polygon Outline, Polygon Plate)>();
        var streetCells = new List<RoadCell>();
        var groups = new Dictionary<Polygon, int>(ReferenceEqualityComparer.Instance); // streets separate the cells inside one bound
        foreach (var tb in shape.Blocks)
        {
            if (!groups.TryGetValue(tb.Bound, out var group)) groups[tb.Bound] = group = groups.Count;
            var k = ctx.SubBlocks(tb.Source.Buildings.Count);
            var raws = k > 1 ? tb.Cell.SplitByAreas(Enumerable.Repeat(1.0, k).ToList(), Polygon.LongestCuts) : [tb.Cell];
            streetCells.AddRange(raws.Select(poly => new RoadCell(poly, group)));

            var parts = raws.Select(raw => BlockPart(raw, tb.Bound, recipe)).ToList();
            allParts.AddRange(parts);
            blocks.Add(new PlacedBlock(tb.Source.BlockId, tb.DistrictId, tb.Source.Name, tb.Color, tb.Cell.Points,
                parts.Select(p => new BlockPart(p.Outline.Points, p.Plate.Points)).ToList()));
            buildings.AddRange(PlaceBuildings(tb.Source, tb.DistrictId, tb.Cell, parts, pitch, maxWidth, heightOf));
        }

        // Roads: main roads between districts (+ template avenues), streets between blocks
        var plan = new StreetPlan(recipe);
        var mainCells = shape.Districts.SelectMany(d => d.Outlines.Select(o => new RoadCell(new Polygon(o), 0))).ToList();
        List<RoadSegment> roads =
        [
            .. plan.SharedEdges(mainCells, recipe.MainRoad, RoadKinds.Main),
            .. shape.Avenues,
            .. plan.SharedEdges(streetCells, recipe.Street, RoadKinds.Street),
        ];
        var streets = plan.Furnish(roads, allParts);

        // Bounds (camera framing, minimap, navigation limits)
        var points = shape.Districts.SelectMany(d => d.Outlines.SelectMany(o => o)).ToList();
        var box = new LayoutBounds(points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Z), points.Max(p => p.Z));
        return new CityLayout(recipe.Name, buildings, shape.Districts, blocks, shape.Plazas, streets, box,
            box.MaxX - box.MinX, box.MaxZ - box.MinZ);
    }

    /// <summary>Sub-block: half a street off the center lines, inside the district bound; sidewalk ring inside.</summary>
    private static (Polygon Outline, Polygon Plate) BlockPart(Polygon raw, Polygon bound, LayoutRecipe recipe)
    {
        var shrunk = raw.Inset(recipe.Street / 2);
        var outline = shrunk.Intersect(bound);
        if (!outline.IsValid) outline = shrunk.IsValid ? shrunk : raw;
        var plate = outline.Inset(LayoutRecipe.Sidewalk);
        return (outline, plate.IsValid ? plate : outline);
    }

    /// <summary>Buildings of a genre over its sub-blocks (by plate area), the part nearest the center first.</summary>
    private static IEnumerable<PlacedBuilding> PlaceBuildings(CityBlock source, string districtId, Polygon cell,
        IReadOnlyList<(Polygon Outline, Polygon Plate)> parts, double pitch, double maxWidth, Func<CityBuilding, double> heightOf)
    {
        var n = source.Buildings.Count;
        var counts = Apportion(n, parts.Select(p => p.Plate.Area).ToList());
        var c = cell.Centroid;
        var order = Enumerable.Range(0, parts.Count)
            .OrderBy(i =>
            {
                var a = parts[i].Plate.Centroid;
                return Vector.Hypot(a.X - c.X, a.Z - c.Z);
            })
            .ToList();

        var output = new List<PlacedBuilding>();
        var next = 0;
        foreach (var i in order)
        {
            var chunk = source.Buildings.Skip(next).Take(counts[i]).ToList();
            next += counts[i];
            if (chunk.Count == 0) continue;
            var lots = parts[i].Plate.Lots(chunk.Count, pitch, maxWidth);
            for (var j = 0; j < chunk.Count; j++)
            {
                var building = chunk[j];
                output.Add(new PlacedBuilding(
                    building.BuildingId, building.Name, districtId, source.BlockId, source.Name,
                    lots.Points[j].X, lots.Points[j].Z,
                    Math.Max(heightOf(building) * LayoutRecipe.Scale, MinHeight),
                    building.Width * LayoutRecipe.Scale,
                    -lots.Rotation, // grid angle in the x/z plane -> rotation around the y axis
                    building.Color));
            }
        }
        return output;
    }

    /// <summary>
    /// Height of a building (API units). Log: as computed by the catalog. Linear: between the same
    /// lowest and highest building, proportional to the qualified plays.
    /// </summary>
    private static Func<CityBuilding, double> HeightScaleOf(IReadOnlyList<CityDistrict> districts, HeightScale scale)
    {
        if (scale == HeightScale.Log) return b => b.Height;
        var all = districts.SelectMany(d => d.Blocks).SelectMany(b => b.Buildings).ToList();
        var (min, max) = (all.Min(b => b.Height), all.Max(b => b.Height));
        var maxPlays = Math.Max(1, all.Max(b => b.QualifiedPlays));
        return b => min + (max - min) * b.QualifiedPlays / maxPlays;
    }

    /// <summary>n items split proportionally to the weights (largest remainders), sum exactly n.</summary>
    internal static int[] Apportion(int n, IReadOnlyList<double> weights)
    {
        var total = 0.0;
        foreach (var w in weights) total += w;
        if (total <= 0) return weights.Select((_, i) => i == 0 ? n : 0).ToArray();
        var exact = weights.Select(w => n * w / total).ToArray();
        var counts = exact.Select(e => (int)Math.Floor(e)).ToArray();
        var order = Enumerable.Range(0, exact.Length).OrderByDescending(i => exact[i] - counts[i]).ToArray();
        var left = n - counts.Sum();
        for (var k = 0; k < left; k++) counts[order[k % order.Length]]++;
        return counts;
    }
}
