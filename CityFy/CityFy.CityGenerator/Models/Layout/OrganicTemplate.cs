using CityFy.CityGenerator.Models.Geometry;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Layout;

/// <summary>
/// "Organic": a Voronoi treemap. The city is an irregular polygon split into the cells of a power
/// diagram (weighted Voronoi) whose weights are tuned until every district cell has the area it
/// needs: irregular districts that tile the city, main roads between them, no empty ground.
/// Inside a district: a square with a roundabout at the center, the blocks around it in wedges
/// (avenues radiating from the square), each wedge split again into blocks by rays and rings.
/// </summary>
public sealed class OrganicTemplate(LayoutContext ctx)
{
    private static readonly double GoldenAngle = Math.PI * (3 - Math.Sqrt(5));
    /// <summary>Wider wedges would not be convex.</summary>
    private static readonly double MaxWedge = 170.0 / 180 * Math.PI;
    private const int MaxWedges = 6;
    private const int OutlineSides = 14;
    private const int TreemapIterations = 120;
    private const int BisectionIterations = 40;

    public static readonly CityTemplate Template = Run;

    private static TemplateResult Run(LayoutContext context) => new OrganicTemplate(context).Build();

    private double Street => ctx.Recipe.Street;
    private double MainRoad => ctx.Recipe.MainRoad;

    public TemplateResult Build()
    {
        var squareArea = Math.PI * Square(ctx.SquareRadius + Street);
        var blockAreas = ctx.Districts.Select(BlockAreasOf).ToList();
        var targets = blockAreas.Select(areas => DistrictTarget(areas, squareArea)).ToList();

        // 1. City outline: irregular convex polygon with the total area
        var total = Sum(targets);
        var seed = new Seed(string.Join('|', ctx.Districts.Select(d => d.DistrictId)) + ctx.Recipe.SeedSuffix);
        var boundary = ctx.Recipe.Shape == CityShape.Circle
            ? CityOutline(Math.Sqrt(total / Math.PI), seed)
            : Polygon.Rectangle(total, ctx.Recipe.Aspect);

        // 2. District cells: Voronoi treemap
        var cells = VoronoiTreemap(boundary, targets);

        var result = new TemplateResult([], [], [], []);
        for (var di = 0; di < ctx.Districts.Count; di++)
        {
            var d = ctx.Districts[di];
            var outline = cells[di];
            var center = outline.Centroid;
            var inner = outline.Inset(MainRoad / 2);
            var bound = inner.IsValid ? inner : outline;
            result.Districts.Add(new PlacedDistrict(d.DistrictId, d.Name, d.Color, center.X, center.Z,
                Math.Sqrt(outline.Area / Math.PI), [outline.Points]));

            // 3. Blocks (cells up to the street center lines): wedges around the square, or (few/very
            //    unequal blocks, or no plazas) plain split of the district
            var pieces = ctx.Recipe.Plazas
                ? BlocksAroundSquare(outline, center, blockAreas[di], ctx.SquareRadius, new Seed(d.DistrictId + ctx.Recipe.SeedSuffix).Unit() * 2 * Math.PI)
                : null;
            if (pieces != null) result.Plazas.Add(new Plaza(center.X, center.Z, ctx.SquareRadius));
            var blockCells = pieces ?? outline.SplitByAreas(blockAreas[di], Polygon.LongestCuts);
            for (var bi = 0; bi < d.Blocks.Count; bi++)
                result.Blocks.Add(new TemplateBlock(d.Blocks[bi], d.DistrictId, d.Color, blockCells[bi], bound));
        }
        return result;
    }

    private List<double> BlockAreasOf(CityDistrict district) => district.Blocks.Select(BlockAreaOf).ToList();

    private double BlockAreaOf(CityBlock block) => ctx.BlockArea(block.Buildings.Count);

    /// <summary>Area of a district: its blocks plus the square, as a square with a main road around.</summary>
    private double DistrictTarget(IReadOnlyList<double> blockAreas, double squareArea) =>
        Square(Math.Sqrt(Sum(blockAreas) + squareArea) + MainRoad);

    /// <summary>Irregular convex outline of the city (hull of a jittered circle), about the requested radius.</summary>
    private static Polygon CityOutline(double radius, Seed seed) =>
        Polygon.ConvexHull(Enumerable.Range(0, OutlineSides).Select(k => OutlinePoint(k, radius, seed)));

    private static Point OutlinePoint(int k, double radius, Seed seed)
    {
        var a = (k + (seed.Unit(k) - 0.5) * 0.5) / OutlineSides * 2 * Math.PI;
        var r = radius * (0.92 + 0.22 * seed.Unit(100 + k));
        return new Point(r * Math.Cos(a), r * Math.Sin(a));
    }

    /// <summary>
    /// Cells of a power diagram inside the boundary with areas proportional to the targets: sites move
    /// to the centroid of their cell and the weights grow/shrink with the area error (Nocaj &amp; Brandes).
    /// The first targets end up near the center. Deterministic.
    /// </summary>
    private static IReadOnlyList<Polygon> VoronoiTreemap(Polygon boundary, IReadOnlyList<double> targets)
    {
        var n = targets.Count;
        if (n == 1) return [boundary];
        var area = boundary.Area;
        var total = Sum(targets);
        var wanted = targets.Select(t => Share(t, total, area)).ToArray();
        var c0 = boundary.Centroid;
        var radius = Math.Sqrt(area / Math.PI);

        // Biggest districts first on a sunflower spiral (center), weights proportional to the area
        var order = Enumerable.Range(0, n).OrderByDescending(i => targets[i]).ToList();
        var sites = new PowerSite[n];
        for (var k = 0; k < n; k++)
        {
            var rho = radius * 0.75 * Math.Sqrt((k + 0.5) / n);
            var a = k * GoldenAngle;
            sites[order[k]] = new PowerSite(c0.X + rho * Math.Cos(a), c0.Z + rho * Math.Sin(a), 0);
        }
        var weights = wanted.Select(InitialWeight).ToArray();

        var cells = new Polygon[n];
        for (var iter = 0; iter < TreemapIterations; iter++)
        {
            // Weights must not swallow a neighbor's site: w_i <= w_j + d_ij^2
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                    if (i != j)
                    {
                        var d2 = Square(sites[i].X - sites[j].X) + Square(sites[i].Z - sites[j].Z);
                        weights[i] = Math.Min(weights[i], weights[j] + d2 * 0.98);
                    }
            var minW = weights.Min();
            for (var i = 0; i < n; i++) weights[i] -= minW; // power diagram unchanged by a common shift
            for (var i = 0; i < n; i++) sites[i].R = Math.Sqrt(weights[i]);

            for (var i = 0; i < n; i++) cells[i] = boundary.PowerCell(sites, i);
            for (var i = 0; i < n; i++)
            {
                var a = cells[i].IsValid ? cells[i].Area : 0;
                if (a > 0)
                {
                    var c = cells[i].Centroid;
                    sites[i].X = c.X;
                    sites[i].Z = c.Z;
                }
                // Grow the weight (circle) of a cell that is too small, shrink one that is too big
                var current = Math.Sqrt(Math.Max(a, wanted[i] * 0.05) / Math.PI);
                var target = Math.Sqrt(wanted[i] / Math.PI);
                var r = Math.Sqrt(weights[i]) + (target - current) * 0.8;
                weights[i] = Square(Math.Max(r, 0));
            }
        }

        // A cell may vanish only in pathological cases: give it a small piece of its biggest neighbor
        var result = new List<Polygon>(n);
        for (var i = 0; i < n; i++)
            result.Add(cells[i].IsValid ? cells[i] : SplitFallback(cells, sites[i]));
        return result;
    }

    private static double Share(double target, double total, double area) => target / total * area;

    private static double InitialWeight(double wantedArea) => wantedArea / Math.PI * 0.2;

    private static Polygon SplitFallback(IReadOnlyList<Polygon> cells, PowerSite site)
    {
        var host = cells[0];
        foreach (var c in cells) if (c.Area > host.Area) host = c;
        var center = host.Centroid;
        var nx = site.X - center.X;
        if (nx == 0 || double.IsNaN(nx)) nx = 1; // JS "|| 1"
        return host.Clip(nx, site.Z - center.Z, nx * center.X + (site.Z - center.Z) * center.Z);
    }

    /// <summary>
    /// Blocks in wedges around a central square. Null when it does not fit (fewer than 3 blocks, or
    /// a block so big its wedge would be wider than 170°).
    /// </summary>
    private IReadOnlyList<Polygon>? BlocksAroundSquare(Polygon area, Point o, IReadOnlyList<double> weights, double squareRadius, double start)
    {
        if (weights.Count < 3) return null;
        var groups = ContiguousGroups(weights, Math.Min(weights.Count, MaxWedges));
        if (groups.Count < 3) return null;
        var groupWeights = groups.Select(g => GroupWeight(g, weights)).ToList();

        var angles = AngularSplit(area, o, groupWeights, start);
        if (angles == null) return null;

        var cells = new Polygon[weights.Count];
        for (var k = 0; k < groups.Count; k++)
        {
            var (a0, a1) = (angles[k], angles[k + 1]);
            var bis = (a0 + a1) / 2;
            var ux = Math.Cos(bis);
            var uz = Math.Sin(bis);
            // Wedge, minus the square (half-plane across the bisector) and half a street on the sides
            var wedge = ClipWedge(area, o, a0, a1);
            wedge = wedge.Clip(-ux, -uz, -(ux * o.X + uz * o.Z + squareRadius + Street / 2));
            if (!wedge.IsValid) return null;
            var pieces = wedge.SplitByAreas(groups[k].Select(i => weights[i]).ToList(), Polygon.RadialCuts(o));
            for (var j = 0; j < groups[k].Count; j++) cells[groups[k][j]] = pieces[j];
        }
        return cells;
    }

    // Left-to-right sum of the group's weights, as the JS loop
    private static double GroupWeight(List<int> group, IReadOnlyList<double> weights)
    {
        var s = 0.0;
        foreach (var i in group) s += weights[i];
        return s;
    }

    /// <summary>Part of a convex polygon between two rays from o (a1 - a0 &lt;= 180°).</summary>
    private static Polygon ClipWedge(Polygon poly, Point o, double a0, double a1)
    {
        var (d0x, d0z) = (Math.Cos(a0), Math.Sin(a0));
        var (d1x, d1z) = (Math.Cos(a1), Math.Sin(a1));
        // after the first ray: cross(d0, p - o) >= 0 ; before the second: cross(d1, p - o) <= 0
        var first = poly.Clip(d0z, -d0x, d0z * o.X - d0x * o.Z);
        return first.Clip(-d1z, d1x, -d1z * o.X + d1x * o.Z);
    }

    /// <summary>Angles (from start, increasing, last = start + 2pi) of wedges with areas proportional to the weights.</summary>
    private static IReadOnlyList<double>? AngularSplit(Polygon poly, Point o, IReadOnlyList<double> weights, double start)
    {
        var full = poly.Area;
        var total = Sum(weights);
        double AreaUpTo(double phi) =>
            phi - start <= Math.PI ? ClipWedge(poly, o, start, phi).Area : full - ClipWedge(poly, o, phi, start + 2 * Math.PI).Area;

        var angles = new List<double> { start };
        var acc = 0.0;
        for (var k = 0; k < weights.Count - 1; k++)
        {
            acc += weights[k];
            var target = acc / total * full;
            var lo = angles[k];
            var hi = start + 2 * Math.PI;
            for (var iter = 0; iter < BisectionIterations; iter++)
            {
                var mid = (lo + hi) / 2;
                if (AreaUpTo(mid) < target) lo = mid;
                else hi = mid;
            }
            angles.Add((lo + hi) / 2);
        }
        angles.Add(start + 2 * Math.PI);
        for (var k = 0; k < weights.Count; k++)
            if (angles[k + 1] - angles[k] > MaxWedge) return null;
        return angles;
    }

    /// <summary>Indexes 0..n-1 in <paramref name="count"/> contiguous groups of similar weight (none empty).</summary>
    private static List<List<int>> ContiguousGroups(IReadOnlyList<double> weights, int count)
    {
        var total = Sum(weights);
        var groups = new List<List<int>>();
        var current = new List<int>();
        var acc = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            var w = weights[i];
            var left = weights.Count - i; // items not yet assigned, this one included
            var groupsLeft = count - groups.Count;
            if (current.Count > 0 && groupsLeft > 1 && (acc + w / 2 > total * (groups.Count + 1) / count || left < groupsLeft))
            {
                groups.Add(current);
                current = [];
            }
            current.Add(i);
            acc += w;
        }
        groups.Add(current);
        return groups;
    }

    private static double Square(double x) => x * x;

    // Left-to-right sum, as the JS reduce
    private static double Sum(IReadOnlyList<double> values)
    {
        var sum = 0.0;
        foreach (var v in values) sum += v;
        return sum;
    }
}
