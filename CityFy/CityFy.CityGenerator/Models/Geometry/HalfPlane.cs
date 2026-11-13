using ServiceDefault.City;

namespace CityFy.CityGenerator.Models.Geometry;

/// <summary>Points with Nx * x + Nz * z &lt;= C.</summary>
public readonly record struct HalfPlane(double Nx, double Nz, double C)
{
    public HalfPlane Opposite => new(-Nx, -Nz, -C);

    public bool Contains(Point p, double eps) => Nx * p.X + Nz * p.Z <= C + eps;
}
