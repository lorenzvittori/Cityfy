using ServiceDefault.Models;

namespace CityFy.Catalog.Services;

public interface IUserDataService
{
    /// <summary>Deletes every play (all sources) and every city of the user. Idempotent.</summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);

    /// <summary>Moves every play to <paramref name="toUserId"/>, deletes the old owner's catalog. Idempotent.</summary>
    Task<UserDataTransfer> TransferAsync(string fromUserId, string toUserId, CancellationToken ct);
}
