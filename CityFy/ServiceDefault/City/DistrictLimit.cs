using ServiceDefault.Errors;

namespace ServiceDefault.City;

/// <summary>What happens to the macro-genres beyond the chosen number of districts.</summary>
public enum ExtraGenres
{
    /// <summary>Merged into one "Other genres" district, which counts as one of the districts.</summary>
    Merge,
    /// <summary>Left out of the city and of its totals, as if the genre filter excluded them.</summary>
    Drop
}

/// <summary>
/// How many districts a city shows (the most listened macro-genres) and what happens to the others.
/// <see cref="Max"/> = <see cref="All"/> keeps every macro-genre. No upper bound: many districts
/// only give the CITY_MANY_DISTRICTS warning. Used by the city generator (request validation) and
/// by the catalog (city build), so both apply the same rules.
/// </summary>
public sealed record DistrictLimit(int Max, ExtraGenres Extra)
{
    public const int All = 0;
    public const string MergeName = "merge";
    public const string DropName = "drop";

    /// <summary>With Merge one district is "Other genres": at least one real genre beside it.</summary>
    private const int MinMerged = 2;
    private const int MinDropped = 1;

    /// <summary>
    /// Validated limit: null max = <paramref name="defaultMax"/>, null or empty extra = merge.
    /// Throws CITY_INVALID_DISTRICTS (400) for a max below the minimum or an unknown extra.
    /// </summary>
    public static DistrictLimit From(int? max, string? extra, int defaultMax)
    {
        var mode = ParseExtra(extra);
        var value = max ?? defaultMax;
        var min = mode == ExtraGenres.Merge ? MinMerged : MinDropped;
        if (value != All && value < min)
            throw new CityFyException(ErrorCodes.CityInvalidDistricts,
                $"maxDistricts must be {All} (all) or at least {min} with extraGenres '{NameOf(mode)}' (got {value})");
        return new DistrictLimit(value, mode);
    }

    /// <summary>True when a city with <paramref name="districts"/> macro-genres goes beyond the limit.</summary>
    public bool IsExceededBy(int districts) => Max != All && districts > Max;

    /// <summary>"merge" / "drop": the form used in requests and stored documents.</summary>
    public string ExtraName => NameOf(Extra);

    public static string NameOf(ExtraGenres extra) => extra == ExtraGenres.Drop ? DropName : MergeName;

    private static ExtraGenres ParseExtra(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra))
            return ExtraGenres.Merge;
        if (string.Equals(extra.Trim(), MergeName, StringComparison.OrdinalIgnoreCase))
            return ExtraGenres.Merge;
        if (string.Equals(extra.Trim(), DropName, StringComparison.OrdinalIgnoreCase))
            return ExtraGenres.Drop;
        throw new CityFyException(ErrorCodes.CityInvalidDistricts,
            $"extraGenres '{extra}' is not available (use {MergeName} or {DropName})");
    }
}
