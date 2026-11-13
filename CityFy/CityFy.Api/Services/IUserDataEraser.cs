using CityFy.Api.Models;

namespace CityFy.Api.Services;

public interface IUserDataEraser
{
    /// <summary>
    /// Deletes everything CityFy keeps about a user, service by service. Throws
    /// USER_DATA_DELETION_INCOMPLETE (502) when a service did not answer: repeating it is safe.
    /// </summary>
    Task<UserDataDeletionResult> DeleteAsync(string userId, string requestedBy, CancellationToken ct);
}
