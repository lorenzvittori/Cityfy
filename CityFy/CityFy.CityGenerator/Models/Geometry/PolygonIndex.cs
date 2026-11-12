using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Geometry;

/// <summary>
/// Convex polygons indexed on a uniform grid of their bounding boxes: which polygons contain a point.
/// Edges are precomputed (thousands of point queries on thousands of polygons stay fast).
/// </summary>
public sealed class PolygonIndex<T>
{
    private readonly List<(IReadOnlyList<HalfPlane> Edges, T Value)> _items = [];
    private readonly Dictionary<(long, long), List<int>> _buckets = new();
    private readonly double _cell;

    public PolygonIndex(IEnumerable<(Polygon Polygon, T Value)> entries)
    {
        var list = entries.Where(e => e.Polygon.IsValid).ToList();
        var sizes = list.Select(e => e.Polygon.MaxX - e.Polygon.MinX).OrderBy(s => s).ToList();
        _cell = Math.Max(sizes.Count > 0 ? sizes[sizes.Count / 2] : 1, 1);

        foreach (var (polygon, value) in list)
        {
            _items.Add((polygon.Edges(), value));
            var i = _items.Count - 1;
            for (var cx = Bucket(polygon.MinX); cx <= Bucket(polygon.MaxX); cx++)
                for (var cz = Bucket(polygon.MinZ); cz <= Bucket(polygon.MaxZ); cz++)
                {
                    if (_buckets.TryGetValue((cx, cz), out var bucket)) bucket.Add(i);
                    else _buckets[(cx, cz)] = [i];
                }
        }
    }

    private long Bucket(double coordinate) => (long)Math.Floor(coordinate / _cell);

    /// <summary>Values of the polygons containing p (strictly inside by eps when eps &lt; 0).</summary>
    public IEnumerable<T> At(Point p, double eps = 1e-6) =>
        _buckets.TryGetValue((Bucket(p.X), Bucket(p.Z)), out var bucket)
            ? bucket.Select(i => _items[i]).Where(it => it.Edges.All(e => e.Contains(p, eps))).Select(it => it.Value)
            : [];
}
