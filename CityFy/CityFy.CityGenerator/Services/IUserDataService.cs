using ServiceDefault.Models;

namespace CityFy.CityGenerator.Services;

public interface IUserDataService
{
    /// <summary>Stops the user's running generations and deletes every generated city. Idempotent.</summary>
    Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct);
}
