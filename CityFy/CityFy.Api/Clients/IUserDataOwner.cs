using ServiceDefault.Models;

namespace CityFy.Api.Clients;

/// <summary>A service that owns part of a user's data and deletes it on DELETE .../users/{userId}/data.</summary>
public interface IUserDataOwner
{
    Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct);
}
