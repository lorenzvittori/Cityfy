using CityFy.CityGenerator.Models.Layout;

namespace CityFy.CityGenerator.Models.Geometry;

/// <summary>
/// Family of parallel or rotating cut lines for a polygon: s in [0, 1] -> half-plane whose area
/// grows with s (see <see cref="Polygon.SplitByAreas"/>).
/// </summary>
public delegate Func<double, HalfPlane> CutFamily(Polygon polygon);

/// <summary>
/// Shape of a city: districts and genre blocks as convex cells up to the road center lines.
/// Sub-blocks, sidewalks, lots and streets come afterwards, the same for every template.
/// </summary>
public delegate TemplateResult CityTemplate(LayoutContext context);
