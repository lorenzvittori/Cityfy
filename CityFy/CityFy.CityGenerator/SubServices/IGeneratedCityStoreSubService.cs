using CityFy.CityGenerator.Models;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.SubServices;

/// <summary>Generated cities of the users: lifecycle of the job, ownership, limits, cancellation.</summary>
public interface IGeneratedCityStoreSubService
{
    /// <summary>A Pending, InProgress or Done city of the user with the same parameters on the same catalog version.</summary>
    Task<GeneratedCity?> FindReusableAsync(string userId, string paramsHash, string catalogVersion, CancellationToken ct);

    /// <summary>Throws CITY_LIMIT_REACHED (409) when the user has CityGenerator:MaxCitiesPerUser cities.</summary>
    Task EnsureBelowLimitAsync(string userId, CancellationToken ct);

    Task<bool> HasAnyAsync(string userId, CancellationToken ct);

    /// <summary>New Pending city.</summary>
    Task<GeneratedCity> CreateAsync(string userId, CityRequest request, string paramsHash, string catalogVersion, DateTime now, CancellationToken ct);

    Task<GeneratedCity?> GetAsync(string cityId, CancellationToken ct);

    /// <summary>The city of the user; throws CITY_NOT_FOUND (404) when missing or of another user.</summary>
    Task<GeneratedCity> GetOwnedAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>Page (from 1, size clamped) of the user's cities, most recent first, without content.</summary>
    Task<(IReadOnlyList<GeneratedCity> Items, int Page, int PageSize, long Total)> ListAsync(string userId, int page, int pageSize, CancellationToken ct);

    /// <summary>Pending or interrupted (InProgress) cities, to queue again at startup.</summary>
    Task<IReadOnlyList<GeneratedCity>> ListUnfinishedAsync(CancellationToken ct);

    /// <summary>Done, Failed or Cancelled: a (duplicate) message for it has nothing to do.</summary>
    bool IsFinished(GeneratedCity city);

    /// <summary>Registers the job as cancellable and marks it InProgress; null when it was cancelled or deleted meanwhile.</summary>
    Task<CancellationTokenSource?> TryStartAsync(string cityId, CancellationToken ct);

    /// <summary>InProgress -> Done. False when it was cancelled or deleted meanwhile (the result is dropped).</summary>
    Task<bool> CompleteAsync(string cityId, CityModelResult result, CityLayout map, CancellationToken ct);

    /// <summary>Pending/InProgress -> Failed.</summary>
    Task FailAsync(string cityId, string errorCode, string message, CancellationToken ct);

    /// <summary>The job is no longer running (always call after <see cref="TryStartAsync"/>).</summary>
    void End(string cityId, CancellationTokenSource cts);

    /// <summary>Pending/InProgress -> Cancelled and the running job stops; throws PROCESS_NOT_CANCELLABLE (409) when finished.</summary>
    Task CancelAsync(GeneratedCity city, CancellationToken ct);

    /// <summary>Cancels the city if still queued or running (finished meanwhile is fine).</summary>
    Task CancelIfActiveAsync(GeneratedCity city, CancellationToken ct);

    Task DeleteAsync(string userId, string cityId, CancellationToken ct);

    /// <summary>Stops the running jobs of the user and deletes every city. Returns how many.</summary>
    Task<long> DeleteAllAsync(string userId, CancellationToken ct);
}
