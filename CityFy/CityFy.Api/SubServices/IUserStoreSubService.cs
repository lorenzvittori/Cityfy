using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

/// <summary>CityFy accounts ('users').</summary>
public interface IUserStoreSubService
{
    /// <summary>Id for a user seen for the first time ("u_" + 96 random bits).</summary>
    string NewUserId();

    Task<CityFyUser?> GetAsync(string userId, CancellationToken ct);

    /// <summary>True when the user was created by this login.</summary>
    Task<bool> RecordLoginAsync(string userId, string lastFmUsername, DateTime now, CancellationToken ct);

    Task<long> DeleteAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct);
}
