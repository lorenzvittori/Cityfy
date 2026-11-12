namespace CityFy.Elaboration.SubServices;

/// <summary>Period of a city: "all" or a number followed by d (days), m (months), y (years), e.g. "30d", "6m", "1y".</summary>
public interface IPeriodSubService
{
    /// <summary>Lower-cased, trimmed period ("all" when empty). False when the format is invalid.</summary>
    bool TryNormalize(string? period, out string normalized);

    /// <summary>Normalized period; throws CITY_INVALID_PERIOD (400) when the format is invalid.</summary>
    string Normalize(string? period);

    /// <summary>Start of the period ending at <paramref name="now"/>; null for "all".</summary>
    DateTime? GetStart(string period, DateTime now);
}
