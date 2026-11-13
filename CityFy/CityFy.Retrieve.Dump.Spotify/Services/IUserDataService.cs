using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IUserDataService
{
    /// <summary>
    /// Stops the user's running/queued imports, then deletes the uploaded archives, extracted files,
    /// legacy collections and upload tasks. Idempotent.
    /// </summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);

    /// <summary>
    /// The uploads (tasks and archives, named by upload id) change owner. Complete false while an
    /// import of the old owner is still running: repeat later. Idempotent.
    /// </summary>
    Task<UserDataTransfer> TransferAsync(string fromUserId, string toUserId, CancellationToken ct);
}
