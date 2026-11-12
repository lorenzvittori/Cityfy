namespace ServiceDefault.City;

// Map of a generated city, computed by CityGenerator and drawn as-is by the frontend.
// Pure geometric output in scene units on the ground plane (x, z): shared by the generator and the
// gateway, exposed by v1 without a copy (see the aspnet-microservices skill).

public sealed record Point(double X, double Z);

public sealed record CityLayout(
    string Template,
    IReadOnlyList<PlacedBuilding> Buildings,
    IReadOnlyList<PlacedDistrict> Districts,
    IReadOnlyList<PlacedBlock> Blocks,
    IReadOnlyList<Plaza> Plazas,
    Streets Streets,
    LayoutBounds Bounds,
    double Width,
    double Depth);

/// <summary>An artist's building. Rotation: around the vertical axis (radians), following the block grid.</summary>
public sealed record PlacedBuilding(
    string ArtistId, string Name, string DistrictId, string BlockId, string GenreName,
    double X, double Z, double Height, double Width, double Rotation, string Color);

/// <summary>X/Z: where the name is shown; Radius: approximate size (label scale); Outlines: ground with half the main road, convex pieces.</summary>
public sealed record PlacedDistrict(string DistrictId, string Name, string Color, double X, double Z, double Radius, IReadOnlyList<IReadOnlyList<Point>> Outlines);

/// <summary>A sub-block: sidewalk ring (Outline) around the plate the buildings stand on.</summary>
public sealed record BlockPart(IReadOnlyList<Point> Outline, IReadOnlyList<Point> Plate);

/// <summary>Cell: ground of the genre up to the middle of the surrounding streets; Parts: sub-blocks separated by inner streets.</summary>
public sealed record PlacedBlock(string BlockId, string DistrictId, string Name, string Color, IReadOnlyList<Point> Cell, IReadOnlyList<BlockPart> Parts);

/// <summary>Roundabout: green island in the middle of a square.</summary>
public sealed record Plaza(double X, double Z, double Radius);

/// <summary>Center line of a road between two intersections. Kind: <see cref="RoadKinds"/>.</summary>
public sealed record RoadSegment(Point A, Point B, double Width, string Kind);

/// <summary>Street furniture on the sidewalk; Angle: direction it faces (radians, x/z plane).</summary>
public sealed record Furniture(double X, double Z, double Angle);

/// <summary>Zebra crossing: center, angle of the road, width of the road.</summary>
public sealed record Crossing(double X, double Z, double Angle, double Width);

/// <summary>Kind: <see cref="SignKinds"/>.</summary>
public sealed record StreetSign(double X, double Z, double Angle, string Kind);

/// <summary>White paint on the asphalt: a thin rectangle (center, angle of its length, length, width).</summary>
public sealed record Stripe(double X, double Z, double Angle, double Length, double Width);

public sealed record Streets(
    IReadOnlyList<RoadSegment> Roads,
    IReadOnlyList<Crossing> Crossings,
    IReadOnlyList<Furniture> Lamps,
    IReadOnlyList<Furniture> TrafficLights,
    IReadOnlyList<StreetSign> Signs,
    IReadOnlyList<Stripe> Markings);

public sealed record LayoutBounds(double MinX, double MaxX, double MinZ, double MaxZ);

public static class RoadKinds
{
    public const string Main = "main";
    public const string Street = "street";
}

public static class SignKinds
{
    public const string Stop = "stop";
    public const string Yield = "yield";
    public const string Crossing = "crossing";
}
