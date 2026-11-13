using ServiceDefault.Models;

namespace CityFy.Api.Clients;

/// <summary>
/// A service that holds original data of a user (plays, uploads) and moves it to another owner on
/// POST .../users/{fromUserId}/transfer (anonymous session → account). Idempotent.
/// </summary>
public interface IUserDataHolder
{
    Task<UserDataTransfer> TransferUserDataAsync(string fromUserId, string toUserId, CancellationToken ct);
}
