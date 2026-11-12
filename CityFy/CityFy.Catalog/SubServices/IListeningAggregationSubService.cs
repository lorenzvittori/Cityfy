using CityFy.Catalog.Models;

namespace CityFy.Catalog.SubServices;

/// <summary>Reads the user's plays ('listening_events').</summary>
public interface IListeningAggregationSubService
{
    /// <summary>
    /// Per-artist totals of the plays in [since, until) (open ends when null); qualified = listened
    /// long enough (City:QualifiedPlayMs).
    /// </summary>
    Task<IReadOnlyList<ArtistPlays>> AggregateAsync(string userId, DateTime? since, DateTime? until, CancellationToken ct);

    /// <summary>Has the user any play at all (any period, any source)?</summary>
    Task<bool> HasPlaysAsync(string userId, CancellationToken ct);
}
