using CityFy.Catalog.SubServices;
using ServiceDefault.Models;

namespace CityFy.Catalog.Services;

public sealed class UserDataService(
    IUserDataDeletionSubService deletion,
    IUserDataTransferSubService transfer,
    ILogger<UserDataService> logger) : IUserDataService
{
    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var deleted = await deletion.DeleteAsync(userId, ct);
        logger.LogInformation("Data of user {UserId} deleted: {Deleted}", userId, deleted.Deleted);
        return deleted;
    }

    public async Task<UserDataTransfer> TransferAsync(string fromUserId, string toUserId, CancellationToken ct)
    {
        var moved = await transfer.TransferAsync(fromUserId, toUserId, ct);
        logger.LogInformation("Data of user {From} moved to {To}: {Moved}", fromUserId, toUserId, moved.Moved);
        return moved;
    }
}
