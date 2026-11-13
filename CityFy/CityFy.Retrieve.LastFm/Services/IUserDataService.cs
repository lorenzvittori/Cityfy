using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Services;

public interface IUserDataService
{
    /// <summary>Stops the user's imports, deletes imports and the linked account. Idempotent.</summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);

    /// <summary>
    /// End of the Last.fm licence (terms 9.3): stops every import and deletes every account and
    /// import of every user, then the memory (tracks playing, activity). Idempotent. The plays are
    /// deleted user by user by the gateway (Catalog).
    /// </summary>
    Task<UserDataDeletion> DeleteEverythingAsync(CancellationToken ct);
}
