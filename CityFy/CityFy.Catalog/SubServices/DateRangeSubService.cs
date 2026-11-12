using ServiceDefault.Errors;

namespace CityFy.Catalog.SubServices;

public sealed class DateRangeSubService : IDateRangeSubService
{
    public (DateTime? From, DateTime? To) Validate(DateTime? from, DateTime? to, DateTime now)
    {
        var f = from.HasValue ? ToUtc(from.Value) : (DateTime?)null;
        var t = to.HasValue ? ToUtc(to.Value) : (DateTime?)null;

        if (f.HasValue && t.HasValue && f.Value >= t.Value)
            throw Invalid($"from ({f:O}) must be before to ({t:O})");
        if (f.HasValue && f.Value > now)
            throw Invalid($"from ({f:O}) is in the future");

        return (f, t);
    }

    private static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    private static CityFyException Invalid(string message) =>
        new(ErrorCodes.CityInvalidRange, message, StatusCodes.Status400BadRequest);
}
