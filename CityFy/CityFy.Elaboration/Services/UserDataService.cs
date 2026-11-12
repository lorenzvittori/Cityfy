using CityFy.Elaboration.SubServices;
using ServiceDefault.Models;

namespace CityFy.Elaboration.Services;

public sealed class UserDataService(IUserDataDeletionSubService deletion, ILogger<UserDataService> logger) : IUserDataService
{
    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var deleted = await deletion.DeleteAsync(userId, ct);
        logger.LogInformation("Data of user {UserId} deleted: {Deleted}", userId, deleted.Deleted);
        return deleted;
    }
}
