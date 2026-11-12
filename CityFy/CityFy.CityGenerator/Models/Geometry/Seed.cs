namespace CityFy.CityGenerator.Models.Geometry;

/// <summary>
/// Deterministic pseudo-random numbers from a text (FNV-1a + avalanche): the same city always
/// gets the same map. Bit-exact port of the frontend hash01 (32-bit unsigned arithmetic).
/// </summary>
public readonly record struct Seed(string Text)
{
    private const double TwoTo32 = 4294967296.0;

    /// <summary>Number in [0, 1).</summary>
    public double Unit(int salt = 0)
    {
        unchecked
        {
            var h = 2166136261u ^ (uint)salt;
            foreach (var c in Text)
                h = (h ^ c) * 16777619u;
            h = (h ^ (h >> 15)) * 2246822507u;
            h = (h ^ (h >> 13)) * 3266489909u;
            return (h ^ (h >> 16)) / TwoTo32;
        }
    }
}
