using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IUserDataService
{
    /// <summary>
    /// Stops the user's running/queued imports, then deletes the uploaded archives, extracted files,
    /// legacy collections and upload tasks. Idempotent.
    /// </summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);
}
