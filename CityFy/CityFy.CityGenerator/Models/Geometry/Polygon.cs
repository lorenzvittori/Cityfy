using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Geometry;

/// <summary>
/// Convex polygon on the ground plane (x, z), points in order (either orientation). Immutable:
/// every operation returns a new polygon. Port of the frontend geometry (same operations in the
/// same order, so the maps match the ones the browser used to compute).
/// </summary>
public sealed class Polygon
{
    private const double Eps = 1e-9;

    public static readonly Polygon Empty = new([]);

    public Polygon(IReadOnlyList<Point> points) => Points = points;

    public IReadOnlyList<Point> Points { get; }

    public int Count => Points.Count;

    /// <summary>At least 3 points.</summary>
    public bool IsValid => Points.Count >= 3;

    public Point this[int i] => Points[i];

    // ------------------------------------------------------------------------------------------
    // Measures
    // ------------------------------------------------------------------------------------------

    public double Area
    {
        get
        {
            var sum = 0.0;
            for (var i = 0; i < Count; i++)
            {
                var a = Points[i];
                var b = Points[(i + 1) % Count];
                sum += a.X * b.Z - b.X * a.Z;
            }
            return Math.Abs(sum) / 2;
        }
    }

    public Point VertexMean
    {
        get
        {
            double sx = 0, sz = 0;
            foreach (var p in Points) sx += p.X;
            foreach (var p in Points) sz += p.Z;
            return new Point(sx / Count, sz / Count);
        }
    }

    /// <summary>Center of mass (vertex mean for a degenerate polygon).</summary>
    public Point Centroid
    {
        get
        {
            double a = 0, cx = 0, cz = 0;
            for (var i = 0; i < Count; i++)
            {
                var p = Points[i];
                var q = Points[(i + 1) % Count];
                var cross = p.X * q.Z - q.X * p.Z;
                a += cross;
                cx += (p.X + q.X) * cross;
                cz += (p.Z + q.Z) * cross;
            }
            if (Math.Abs(a) < Eps) return Count > 0 ? VertexMean : new Point(0, 0);
            return new Point(cx / (3 * a), cz / (3 * a));
        }
    }

    public double MinX => Points.Min(p => p.X);
    public double MaxX => Points.Max(p => p.X);
    public double MinZ => Points.Min(p => p.Z);
    public double MaxZ => Points.Max(p => p.Z);

    // ------------------------------------------------------------------------------------------
    // Clipping
    // ------------------------------------------------------------------------------------------

    /// <summary>Part of the polygon where Nx * x + Nz * z &lt;= C.</summary>
    public Polygon Clip(HalfPlane h) => Clip(h.Nx, h.Nz, h.C);

    public Polygon Clip(double nx, double nz, double c)
    {
        var output = new List<Point>(Count + 1);
        for (var i = 0; i < Count; i++)
        {
            var a = Points[i];
            var b = Points[(i + 1) % Count];
            var da = nx * a.X + nz * a.Z - c;
            var db = nx * b.X + nz * b.Z - c;
            if (da <= 0) output.Add(a);
            if ((da < 0 && db > 0) || (da > 0 && db < 0))
            {
                var t = da / (da - db);
                output.Add(new Point(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t));
            }
        }
        return new Polygon(output);
    }

    /// <summary>Edges as half-planes with the outward normal.</summary>
    public IReadOnlyList<HalfPlane> Edges()
    {
        var center = VertexMean;
        var edges = new List<HalfPlane>(Count);
        for (var i = 0; i < Count; i++)
        {
            var a = Points[i];
            var b = Points[(i + 1) % Count];
            var nx = b.Z - a.Z;
            var nz = a.X - b.X;
            var len = Vector.Hypot(nx, nz);
            if (len < Eps) continue;
            nx /= len;
            nz /= len;
            if (nx * (center.X - a.X) + nz * (center.Z - a.Z) > 0)
            {
                nx = -nx; // make it point outwards
                nz = -nz;
            }
            edges.Add(new HalfPlane(nx, nz, nx * a.X + nz * a.Z));
        }
        return edges;
    }

    /// <summary>Shrunk by d on every side (empty when it vanishes).</summary>
    public Polygon Inset(double d)
    {
        if (!IsValid) return Empty;
        var output = this;
        foreach (var e in Edges())
        {
            output = output.Clip(e.Nx, e.Nz, e.C - d);
            if (!output.IsValid) return Empty;
        }
        return output;
    }

    public bool Contains(Point p, double eps = 1e-6) => IsValid && Edges().All(e => e.Contains(p, eps));

    public Polygon Intersect(Polygon other)
    {
        if (!IsValid || !other.IsValid) return Empty;
        var output = this;
        foreach (var e in other.Edges())
        {
            output = output.Clip(e);
            if (!output.IsValid) return Empty;
        }
        return output;
    }

    // ------------------------------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------------------------------

    /// <summary>Rectangle centered on the origin with the given area and width / depth.</summary>
    public static Polygon Rectangle(double area, double aspect)
    {
        var width = Math.Sqrt(area * aspect);
        var depth = area / width;
        return new Polygon(
        [
            new Point(-width / 2, -depth / 2),
            new Point(width / 2, -depth / 2),
            new Point(width / 2, depth / 2),
            new Point(-width / 2, depth / 2),
        ]);
    }

    /// <summary>Regular polygon centered on the origin with the given area (circle approximation).</summary>
    public static Polygon Regular(double area, int sides)
    {
        var r = Math.Sqrt(area / (sides / 2.0 * Math.Sin(2 * Math.PI / sides)));
        return new Polygon(Enumerable.Range(0, sides).Select(k => RegularVertex(k, r, sides)).ToList());
    }

    private static Point RegularVertex(int k, double r, int sides) =>
        new(r * Math.Cos(2 * Math.PI * k / sides), r * Math.Sin(2 * Math.PI * k / sides));

    /// <summary>Convex hull (monotone chain), counter-clockwise.</summary>
    public static Polygon ConvexHull(IEnumerable<Point> points)
    {
        var pts = points.OrderBy(p => p.X).ThenBy(p => p.Z).ToList(); // stable, as the JS sort
        if (pts.Count < 3) return new Polygon(pts);

        static double Cross(Point o, Point a, Point b) => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);

        var lower = new List<Point>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<Point>();
        for (var i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        return new Polygon([.. lower.Take(lower.Count - 1), .. upper.Take(upper.Count - 1)]);
    }

    /// <summary>
    /// Cell of site i in the power diagram of the sites (power of p for site j: |p - c_j|^2 - r_j^2),
    /// intersected with this polygon. Empty when it vanishes.
    /// </summary>
    public Polygon PowerCell(IReadOnlyList<PowerSite> sites, int i)
    {
        var cell = this;
        var a = sites[i];
        for (var j = 0; j < sites.Count && cell.IsValid; j++)
        {
            if (j == i) continue;
            var b = sites[j];
            // |p-a|^2 - ra^2 <= |p-b|^2 - rb^2  <=>  2(b-a).p <= |b|^2 - |a|^2 + ra^2 - rb^2
            var nx = b.X - a.X;
            var nz = b.Z - a.Z;
            if (Math.Abs(nx) < Eps && Math.Abs(nz) < Eps) continue;
            var c = (b.X * b.X + b.Z * b.Z - a.X * a.X - a.Z * a.Z + a.R * a.R - b.R * b.R) / 2;
            cell = cell.Clip(nx, nz, c);
        }
        return cell.IsValid ? cell : Empty;
    }

    // ------------------------------------------------------------------------------------------
    // Area partition: recursive bisection (treemap on polygons)
    // ------------------------------------------------------------------------------------------

    /// <summary>Axis-aligned cuts across the longest side: rectangles stay rectangles (grid city).</summary>
    public static readonly CutFamily AxisCuts = poly =>
    {
        double minX = poly.MinX, maxX = poly.MaxX, minZ = poly.MinZ, maxZ = poly.MaxZ;
        return maxX - minX >= maxZ - minZ
            ? s => new HalfPlane(1, 0, minX + s * (maxX - minX))
            : s => new HalfPlane(0, 1, minZ + s * (maxZ - minZ));
    };

    /// <summary>Cuts across the longest direction of the polygon (any angle): irregular but compact pieces.</summary>
    public static readonly CutFamily LongestCuts = poly =>
    {
        double bux = 1, buz = 0, bmin = 0, bmax = 0;
        for (var k = 0; k < 12; k++)
        {
            var angle = k / 12.0 * Math.PI;
            var ux = Math.Cos(angle);
            var uz = Math.Sin(angle);
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var p in poly.Points)
            {
                var proj = ux * p.X + uz * p.Z;
                min = Math.Min(min, proj);
                max = Math.Max(max, proj);
            }
            if (max - min > bmax - bmin + Eps) (bux, buz, bmin, bmax) = (ux, uz, min, max); // ties: the first direction wins
        }
        return s => new HalfPlane(bux, buz, bmin + s * (bmax - bmin));
    };

    /// <summary>
    /// Cuts seen from a center: rays from o (angular split) or lines across the radius (rings),
    /// whichever splits the longest dimension. The polygon must lie in a wedge &lt; 180°.
    /// </summary>
    public static CutFamily RadialCuts(Point o) => poly =>
    {
        var c = poly.Centroid;
        var bis = Math.Atan2(c.Z - o.Z, c.X - o.X);
        var ux = Math.Cos(bis);
        var uz = Math.Sin(bis);
        double aMin = double.PositiveInfinity, aMax = double.NegativeInfinity, dMin = double.PositiveInfinity, dMax = double.NegativeInfinity;
        foreach (var p in poly.Points)
        {
            var a = Math.Atan2(p.Z - o.Z, p.X - o.X) - bis;
            a = Math.Atan2(Math.Sin(a), Math.Cos(a)); // (-pi, pi]
            var d = ux * (p.X - o.X) + uz * (p.Z - o.Z);
            aMin = Math.Min(aMin, a);
            aMax = Math.Max(aMax, a);
            dMin = Math.Min(dMin, d);
            dMax = Math.Max(dMax, d);
        }
        var arc = (dMin + dMax) / 2 * (aMax - aMin);
        if (arc >= dMax - dMin)
        {
            // ray at angle phi: keep the points before it (cross(dir, p - o) <= 0)
            return s =>
            {
                var phi = bis + aMin + s * (aMax - aMin);
                var nx = -Math.Sin(phi);
                var nz = Math.Cos(phi);
                return new HalfPlane(nx, nz, nx * o.X + nz * o.Z);
            };
        }
        return s => new HalfPlane(ux, uz, ux * o.X + uz * o.Z + dMin + s * (dMax - dMin));
    };

    /// <summary>
    /// Convex pieces with areas proportional to the weights, in order (first weights on the side
    /// where the cuts start). Recursive halving keeps the pieces compact.
    /// </summary>
    public IReadOnlyList<Polygon> SplitByAreas(IReadOnlyList<double> weights, CutFamily family)
    {
        if (weights.Count == 0) return [];
        if (weights.Count == 1 || !IsValid) return Enumerable.Repeat(this, weights.Count).ToList();

        var total = SumOf(weights, 0, weights.Count);
        int k = 1;
        var best = double.PositiveInfinity;
        var acc = 0.0;
        for (var i = 1; i < weights.Count; i++)
        {
            acc += weights[i - 1];
            var diff = Math.Abs(acc - total / 2);
            if (diff < best)
            {
                best = diff;
                k = i;
            }
        }
        var ratio = SumOf(weights, 0, k) / total;

        var cut = family(this);
        var full = Area;
        double lo = 0, hi = 1;
        for (var iter = 0; iter < 40; iter++)
        {
            var mid = (lo + hi) / 2;
            if (Clip(cut(mid)).Area / full < ratio) lo = mid;
            else hi = mid;
        }
        var h = cut((lo + hi) / 2);
        var a = Clip(h);
        var b = Clip(h.Opposite);
        return
        [
            .. (a.IsValid ? a : this).SplitByAreas(weights.Take(k).ToList(), family),
            .. (b.IsValid ? b : this).SplitByAreas(weights.Skip(k).ToList(), family),
        ];
    }

    // Left-to-right sum (as the JS reduce: the order of floating-point additions matters)
    private static double SumOf(IReadOnlyList<double> values, int start, int count)
    {
        var sum = 0.0;
        for (var i = start; i < start + count; i++) sum += values[i];
        return sum;
    }

    // ------------------------------------------------------------------------------------------
    // Lots inside a block
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Grid of n lots in this block, aligned with its longest side, spread over the whole block
    /// (spacing between pitch and 1.6 pitch), center first.
    /// </summary>
    public Lots Lots(int n, double pitch, double buildingWidth)
    {
        var rotation = 0.0;
        var longest = -1.0;
        for (var i = 0; i < Count; i++)
        {
            var a = Points[i];
            var b = Points[(i + 1) % Count];
            var len = Vector.Hypot(b.X - a.X, b.Z - a.Z);
            if (len > longest + Eps) // equal sides (to the last bits): the first one wins
            {
                longest = len;
                rotation = Math.Atan2(b.Z - a.Z, b.X - a.X);
            }
        }
        var ux = Math.Cos(rotation);
        var uz = Math.Sin(rotation);
        var center = IsValid ? Centroid : new Point(0, 0);
        // Distance rounded to 1e-6 (JS Math.round): lots at the same distance keep the grid order,
        // whatever the last bits of sin/cos (they differ between runtimes)
        double RoundedDistance(Point p) => Math.Floor(Vector.Hypot(p.X - center.X, p.Z - center.Z) * 1e6 + 0.5);
        List<Point> ByDistance(List<Point> pts) => pts.OrderBy(RoundedDistance).ToList();

        List<Point> Grid(double step, double ou, double ov, Polygon? area, int extent)
        {
            var edges = area?.Edges() ?? [];
            var pts = new List<Point>();
            for (var i = -extent; i <= extent; i++)
                for (var j = -extent; j <= extent; j++)
                {
                    var u = (i + ou) * step;
                    var v = (j + ov) * step;
                    var p = new Point(center.X + u * ux - v * uz, center.Z + u * uz + v * ux);
                    if (edges.All(e => e.Contains(p, 1e-6))) pts.Add(p);
                }
            return pts;
        }

        // 1. inside the block, widest spacing that still holds n lots: first with the building footprint
        //    fully inside, then (narrow blocks) with only the building center inside
        var radius = 0.0;
        foreach (var p in Points) radius = Math.Max(radius, Vector.Hypot(p.X - center.X, p.Z - center.Z));
        var minStep = buildingWidth * 1.05;
        (double, double)[] offsets = [(0, 0), (0.5, 0), (0, 0.5), (0.5, 0.5)];
        foreach (var area in new[] { Inset(buildingWidth / 2), this })
        {
            if (!area.IsValid) continue;
            var step = Math.Min(pitch * 1.6, Math.Max(pitch, Math.Sqrt(area.Area / n) * 0.95));
            for (; step >= minStep - 1e-9; step *= 0.92)
            {
                var extent = (int)Math.Ceiling(radius / step) + 1;
                var best = new List<Point>();
                foreach (var (ou, ov) in offsets)
                {
                    var pts = Grid(step, ou, ov, area, extent);
                    if (pts.Count > best.Count) best = pts;
                }
                if (best.Count >= n) return new Lots(ByDistance(best).Take(n).ToList(), rotation);
            }
        }

        // 2. fallback (block too small): grid around the center, ignoring the outline
        var fallbackExtent = (int)Math.Ceiling(Math.Sqrt(n)) + 1;
        return new Lots(ByDistance(Grid(pitch, 0, 0, null, fallbackExtent)).Take(n).ToList(), rotation);
    }
}

/// <summary>Lot centers (center of the block first) and the angle of the lot grid.</summary>
public sealed record Lots(IReadOnlyList<Point> Points, double Rotation);

/// <summary>Site of a power diagram: center and radius (weight = R²). Mutable while the treemap converges.</summary>
public sealed class PowerSite(double x, double z, double r)
{
    public double X { get; set; } = x;
    public double Z { get; set; } = z;
    public double R { get; set; } = r;
}
