using ServiceDefault.Models;

namespace CityFy.Catalog.SubServices;

public interface IUserDataDeletionSubService
{
    /// <summary>Deletes every play (all sources) and every city of the user. Idempotent.</summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);
}
