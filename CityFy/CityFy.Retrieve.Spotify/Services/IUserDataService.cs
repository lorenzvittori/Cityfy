using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Services;

public interface IUserDataService
{
    /// <summary>
    /// Stops the user's running/queued retrieves, then deletes the jobs and the linked account
    /// (refresh token included). Idempotent.
    /// </summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);
}
