using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.SubServices;

/// <summary>Reads the user's plays ('listening_events').</summary>
public interface IListeningAggregationSubService
{
    /// <summary>Per-artist totals since a date (all history when null); qualified = listened long enough (City:QualifiedPlayMs).</summary>
    Task<IReadOnlyList<ArtistPlays>> AggregateAsync(string userId, DateTime? since, CancellationToken ct);

    /// <summary>Has the user any play at all (any period, any source)?</summary>
    Task<bool> HasPlaysAsync(string userId, CancellationToken ct);
}
