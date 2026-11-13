namespace CityFy.Catalog.SubServices;

/// <summary>Date range of a city request: [From, To), open ends when null.</summary>
public interface IDateRangeSubService
{
    /// <summary>
    /// Dates as UTC (unspecified kind = UTC). Throws CITY_INVALID_RANGE (400) when From is not
    /// before To or From is in the future.
    /// </summary>
    (DateTime? From, DateTime? To) Validate(DateTime? from, DateTime? to, DateTime now);
}
