using System.Text.RegularExpressions;

namespace CityFy.Elaboration.Services;

/// <summary>Period of a city: "all" or a number followed by d (days), m (months), y (years), e.g. "30d", "6m", "1y".</summary>
public static partial class Periods
{
    public const string All = "all";

    [GeneratedRegex(@"^(?<n>[1-9]\d{0,3})(?<unit>[dmy])$")]
    private static partial Regex PeriodRegex();

    public static bool TryNormalize(string? period, out string normalized)
    {
        normalized = (period ?? All).Trim().ToLowerInvariant();
        return normalized == All || PeriodRegex().IsMatch(normalized);
    }

    /// <summary>Start of the period ending at <paramref name="now"/>; null for "all".</summary>
    public static DateTime? GetStart(string period, DateTime now)
    {
        if (period == All) return null;

        var m = PeriodRegex().Match(period);
        if (!m.Success) throw new ArgumentException($"Invalid period '{period}'", nameof(period));

        var n = int.Parse(m.Groups["n"].Value);
        return m.Groups["unit"].Value switch
        {
            "d" => now.AddDays(-n),
            "m" => now.AddMonths(-n),
            _ => now.AddYears(-n)
        };
    }
}
