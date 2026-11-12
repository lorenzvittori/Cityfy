using System.Text.RegularExpressions;
using ServiceDefault.Errors;

namespace CityFy.Elaboration.SubServices;

public sealed partial class PeriodSubService : IPeriodSubService
{
    public const string All = "all";

    [GeneratedRegex(@"^(?<n>[1-9]\d{0,3})(?<unit>[dmy])$")]
    private static partial Regex PeriodRegex();

    public bool TryNormalize(string? period, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(period) ? All : period.Trim().ToLowerInvariant();
        return normalized == All || PeriodRegex().IsMatch(normalized);
    }

    public string Normalize(string? period) =>
        TryNormalize(period, out var normalized)
            ? normalized
            : throw new CityFyException(ErrorCodes.CityInvalidPeriod, "period must be 'all' or <n>d|m|y, e.g. 30d, 6m, 1y", StatusCodes.Status400BadRequest);

    public DateTime? GetStart(string period, DateTime now)
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
