namespace CityFy.CityGenerator.Models.Geometry;

/// <summary>Direction / displacement on the ground plane.</summary>
public readonly record struct Vector(double X, double Z)
{
    /// <summary>
    /// Euclidean length computed as the browser's Math.hypot does (scaling by the largest component
    /// plus Kahan summation), so ports of browser geometry give the same numbers to the last bit.
    /// </summary>
    public double Length
    {
        get
        {
            var ax = Math.Abs(X);
            var az = Math.Abs(Z);
            if (double.IsNaN(ax) || double.IsNaN(az)) return double.IsInfinity(ax) || double.IsInfinity(az) ? double.PositiveInfinity : double.NaN;
            var max = Math.Max(ax, az);
            if (double.IsPositiveInfinity(max)) return double.PositiveInfinity;
            if (max == 0) return 0;

            double sum = 0, compensation = 0;
            foreach (var v in (ReadOnlySpan<double>)[ax, az])
            {
                var n = v / max;
                var summand = n * n - compensation;
                var preliminary = sum + summand;
                compensation = preliminary - sum - summand;
                sum = preliminary;
            }
            return Math.Sqrt(sum) * max;
        }
    }

    public static double Hypot(double x, double z) => new Vector(x, z).Length;
}
