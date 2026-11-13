using CityFy.CityGenerator.Models;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Repositories;

/// <summary>'generated_cities'.</summary>
public interface IGeneratedCityRepository
{
    Task InsertAsync(GeneratedCity city, CancellationToken ct);

    Task<GeneratedCity?> GetAsync(string cityId, CancellationToken ct);

    /// <summary>A city of the user with these parameters on this catalog version, in one of the statuses (most recent first).</summary>
    Task<GeneratedCity?> FindAsync(string userId, string paramsHash, string catalogVersion, IReadOnlyCollection<ProcessingStatus> statuses, CancellationToken ct);

    Task<long> CountByUserAsync(string userId, CancellationToken ct);

    /// <summary>Most recent first, without the city content.</summary>
    Task<IReadOnlyList<GeneratedCity>> ListByUserAsync(string userId, int skip, int take, CancellationToken ct);

    Task<IReadOnlyList<GeneratedCity>> ListByStatusAsync(IReadOnlyCollection<ProcessingStatus> statuses, CancellationToken ct);

    /// <summary>
    /// Sets the status only if the city is in one of <paramref name="from"/> (a Cancelled city is
    /// never overwritten by a late update). True when updated.
    /// </summary>
    Task<bool> TryUpdateStatusAsync(string cityId, IReadOnlyCollection<ProcessingStatus> from, ProcessingStatus to,
        string? errorCode, string? message, DateTime now, CancellationToken ct);

    /// <summary>InProgress -> Done with the city and its map. False when cancelled or deleted meanwhile.</summary>
    Task<bool> CompleteAsync(string cityId, CityModel city, CityLayout map, string catalogVersion, DateTime now, CancellationToken ct);

    Task<bool> DeleteAsync(string userId, string cityId, CancellationToken ct);

    Task<long> DeleteByUserAsync(string userId, CancellationToken ct);
}
