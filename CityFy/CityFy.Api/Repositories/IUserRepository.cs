using CityFy.Api.Models;

namespace CityFy.Api.Repositories;

public interface IUserRepository
{
    Task<CityFyUser?> GetAsync(string userId, CancellationToken ct);

    /// <summary>Creates the user the first time, otherwise updates LastLoginAt. True when created.</summary>
    Task<bool> RecordLoginAsync(string userId, string lastFmUsername, DateTime now, CancellationToken ct);

    Task<long> DeleteAsync(string userId, CancellationToken ct);

    /// <summary>Ids of every account.</summary>
    Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct);
}
