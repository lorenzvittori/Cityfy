using System.Globalization;
using CityFy.CityGenerator.Models.Geometry;
using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Layout;

/// <summary>A cell bordered by roads: cells of the same group are separated by roads of one level (e.g. the blocks of a district).</summary>
public sealed record RoadCell(Polygon Polygon, int Group);

/// <summary>
/// Streets of the city, derived from the geometry. Every road runs along the border between two
/// cells: the edge shared by two district outlines is the center line of a main road, the edge
/// shared by two block cells of the same district is the center line of a street. Shared edges are
/// found by probing just across each edge, then collinear pieces are merged into segments between
/// intersections. On every segment: center line markings, zebra crossings near both ends, street
/// lamps on the sidewalks, traffic lights (main roads) or stop/give-way signs (streets) at the ends.
/// </summary>
public sealed class StreetPlan(LayoutRecipe recipe)
{
    private const double Probe = 0.4;          // distance of the probe across an edge
    private const double MinEdge = 0.5;        // shorter edges are not roads
    private const double CrossingDepth = 1.6;  // zebra crossing size along the road
    private const double Dash = 1.4;
    private const double DashGap = 1.4;
    private const double Line = 0.12;

    /// <summary>Distance from the intersection center to the middle of the zebra crossing.</summary>
    private double EndGap => recipe.MainRoad / 2 + LayoutRecipe.Sidewalk + CrossingDepth / 2 + 0.4;

    /// <summary>Center lines of the roads between cells of the same group (merged, between intersections).</summary>
    public IReadOnlyList<RoadSegment> SharedEdges(IReadOnlyList<RoadCell> cells, double width, string kind)
    {
        var index = new PolygonIndex<int>(cells.Select((c, i) => (c.Polygon, i)));
        var pieces = new List<(Point A, Point B)>();

        for (var i = 0; i < cells.Count; i++)
        {
            var poly = cells[i].Polygon;
            if (!poly.IsValid) continue;
            double sx = 0, sz = 0;
            foreach (var p in poly.Points) sx += p.X;
            foreach (var p in poly.Points) sz += p.Z;
            var cx = sx / poly.Count;
            var cz = sz / poly.Count;
            for (var k = 0; k < poly.Count; k++)
            {
                var a = poly[k];
                var b = poly[(k + 1) % poly.Count];
                var len = Vector.Hypot(b.X - a.X, b.Z - a.Z);
                if (len < MinEdge) continue;
                var nx = (b.Z - a.Z) / len;
                var nz = -(b.X - a.X) / len;
                var mx = (a.X + b.X) / 2;
                var mz = (a.Z + b.Z) / 2;
                if (nx * (cx - mx) + nz * (cz - mz) > 0) (nx, nz) = (-nx, -nz); // outwards
                var probe = new Point(mx + nx * Probe, mz + nz * Probe);
                var group = cells[i].Group;
                var self = i;
                if (index.At(probe, -1e-6).Any(j => j != self && cells[j].Group == group))
                    pieces.Add((a, b));
            }
        }

        return MergeCollinear(pieces).Select(s => new RoadSegment(s.A, s.B, width, kind)).ToList();
    }

    /// <summary>Unites overlapping collinear pieces (each shared edge is seen from both sides).</summary>
    public static IReadOnlyList<(Point A, Point B)> MergeCollinear(IEnumerable<(Point A, Point B)> pieces)
    {
        var keys = new List<(long, long)>(); // insertion order, as the JS Map
        var lines = new Dictionary<(long, long), (double Ux, double Uz, double C, List<(double S, double E)> Intervals)>();
        foreach (var (a, b) in pieces)
        {
            var ux = b.X - a.X;
            var uz = b.Z - a.Z;
            var len = Vector.Hypot(ux, uz);
            ux /= len;
            uz /= len;
            if (ux < -1e-9 || (Math.Abs(ux) <= 1e-9 && uz < 0)) (ux, uz) = (-ux, -uz); // one direction per line
            var c = -uz * a.X + ux * a.Z; // offset along the normal
            var key = (JsRound(Math.Atan2(uz, ux) * 500), JsRound(c * 10));
            if (!lines.TryGetValue(key, out var line))
            {
                line = (ux, uz, c, []);
                lines[key] = line;
                keys.Add(key);
            }
            var ta = ux * a.X + uz * a.Z;
            var tb = ux * b.X + uz * b.Z;
            line.Intervals.Add((Math.Min(ta, tb), Math.Max(ta, tb)));
        }

        var output = new List<(Point, Point)>();
        foreach (var key in keys)
        {
            var (ux, uz, c, intervals) = lines[key];
            var sorted = intervals.OrderBy(iv => iv.S).ToList(); // stable, as the JS sort
            var (s, e) = sorted[0];
            void Flush()
            {
                if (e - s >= MinEdge)
                    output.Add((new Point(ux * s - uz * c, uz * s + ux * c), new Point(ux * e - uz * c, uz * e + ux * c)));
            }
            foreach (var (s2, e2) in sorted.Skip(1))
            {
                if (s2 <= e + 0.05) e = Math.Max(e, e2);
                else
                {
                    Flush();
                    (s, e) = (s2, e2);
                }
            }
            Flush();
        }
        return output;
    }

    /// <summary>
    /// Crossings, lamps, lights and signs along the roads, plus the paint. Furniture is placed only
    /// where there is a sidewalk (inside a block part, outside its plate).
    /// </summary>
    public Streets Furnish(IReadOnlyList<RoadSegment> roads, IReadOnlyList<(Polygon Outline, Polygon Plate)> parts)
    {
        var sidewalks = new PolygonIndex<Polygon>(parts.Select(p => (p.Outline, p.Plate)));
        bool OnSidewalk(Point p) => sidewalks.At(p).Any(plate => !plate.Contains(p));

        var crossings = new List<Crossing>();
        var lamps = new List<Furniture>();
        var lights = new List<Furniture>();
        var signs = new List<StreetSign>();
        var endGap = EndGap;
        const double sidewalk = LayoutRecipe.Sidewalk;

        for (var ri = 0; ri < roads.Count; ri++)
        {
            var road = roads[ri];
            var len = Vector.Hypot(road.B.X - road.A.X, road.B.Z - road.A.Z);
            var ux = (road.B.X - road.A.X) / len;
            var uz = (road.B.Z - road.A.Z) / len;
            var (lx, lz) = (-uz, ux); // left normal
            var half = road.Width / 2;
            var angle = Math.Atan2(uz, ux);
            Point At(double t, double side, double offset) =>
                new(road.A.X + ux * t + lx * side * offset, road.A.Z + uz * t + lz * side * offset);
            var curb = half + sidewalk * 0.5;
            if (len < 2 * endGap + 2) continue; // too short: just asphalt

            // Zebra crossings near both ends
            foreach (var t in new[] { endGap, len - endGap })
            {
                var c = At(t, 0, 0);
                crossings.Add(new Crossing(c.X, c.Z, angle, road.Width));
            }

            // Lamps on alternate sides, facing the road
            double lampSide = ri % 2 != 0 ? 1 : -1;
            if (recipe.Lamps > 0)
            for (var t = endGap + 3; t <= len - endGap - 3; t += recipe.LampSpacing)
            {
                var p = At(t, lampSide, curb);
                if (OnSidewalk(p)) lamps.Add(new Furniture(p.X, p.Z, Math.Atan2(-lz * lampSide, -lx * lampSide)));
                lampSide = -lampSide;
            }

            // Ends: drivers keep right. Approaching B (direction +u) the right side is -left, and vice versa.
            (double T, double Side, double Faces)[] ends =
            [
                (len - endGap - CrossingDepth, -1, angle + Math.PI),
                (endGap + CrossingDepth, 1, angle),
            ];
            for (var k = 0; k < ends.Length; k++)
            {
                var end = ends[k];
                var p = At(end.T, end.Side, curb);
                if (!OnSidewalk(p)) continue;
                if (road.Kind == RoadKinds.Main)
                    lights.Add(new Furniture(p.X, p.Z, end.Faces));
                else
                {
                    var kind = new Seed($"{JsFixed1(p.X)},{JsFixed1(p.Z)}").Unit() < 0.5 ? SignKinds.Stop : SignKinds.Yield;
                    signs.Add(new StreetSign(p.X, p.Z, end.Faces, kind));
                }
                // Pedestrian crossing sign on the other side of main roads, next to the zebra
                if (road.Kind == RoadKinds.Main)
                {
                    var q = At(k == 0 ? len - endGap : endGap, -end.Side, curb);
                    if (OnSidewalk(q)) signs.Add(new StreetSign(q.X, q.Z, end.Faces, SignKinds.Crossing));
                }
            }
        }

        return new Streets(roads, crossings, lamps, lights, signs, Markings(roads, crossings));
    }

    /// <summary>
    /// Horizontal markings: double continuous center line on main roads, dashed center line on
    /// streets (both stop before the crossings), zebra stripes across the road at the crossings.
    /// </summary>
    public IReadOnlyList<Stripe> Markings(IReadOnlyList<RoadSegment> roads, IReadOnlyList<Crossing> crossings)
    {
        var output = new List<Stripe>();
        var endGap = EndGap;
        foreach (var road in roads)
        {
            var len = Vector.Hypot(road.B.X - road.A.X, road.B.Z - road.A.Z);
            if (len < 2 * endGap + 2) continue;
            var ux = (road.B.X - road.A.X) / len;
            var uz = (road.B.Z - road.A.Z) / len;
            var angle = Math.Atan2(uz, ux);
            var from = endGap + CrossingDepth / 2 + 0.6;
            var to = len - from;
            if (to <= from) continue;
            Point At(double t, double off) => new(road.A.X + ux * t - uz * off, road.A.Z + uz * t + ux * off);

            if (road.Kind == RoadKinds.Main)
            {
                foreach (var off in new[] { -0.15, 0.15 })
                {
                    var p = At((from + to) / 2, off);
                    output.Add(new Stripe(p.X, p.Z, angle, to - from, Line));
                }
            }
            else
            {
                for (var t = from; t + Dash <= to; t += Dash + DashGap)
                {
                    var p = At(t + Dash / 2, 0);
                    output.Add(new Stripe(p.X, p.Z, angle, Dash, Line));
                }
            }
        }

        // Zebra: stripes along the road, side by side across it
        foreach (var c in crossings)
        {
            var count = Math.Max(2, (int)Math.Floor((c.Width - 0.4) / 0.8));
            var (px, pz) = (-Math.Sin(c.Angle), Math.Cos(c.Angle));
            for (var k = 0; k < count; k++)
            {
                var off = (k - (count - 1) / 2.0) * 0.8;
                output.Add(new Stripe(c.X + px * off, c.Z + pz * off, c.Angle, CrossingDepth, 0.42));
            }
        }
        return output;
    }

    /// <summary>JS Math.round: halves go towards +infinity.</summary>
    private static long JsRound(double x) => (long)Math.Floor(x + 0.5);

    /// <summary>JS Number.toFixed(1) (used as the seed of a sign, so it must give the same text).</summary>
    private static string JsFixed1(double x) => x.ToString("F1", CultureInfo.InvariantCulture);
}
