using CityFy.Catalog.Repositories;
using ServiceDefault.Listening;
using ServiceDefault.Models;

namespace CityFy.Catalog.SubServices;

public sealed class UserDataTransferSubService(IListeningEventTransfer plays, ICatalogRepository repo) : IUserDataTransferSubService
{
    public async Task<UserDataTransfer> TransferAsync(string fromUserId, string toUserId, CancellationToken ct)
    {
        var moved = await plays.TransferAsync(fromUserId, toUserId, ct);
        // Without plays left, deleting the old owner removes only its derived data (catalog, placements)
        var (_, artists) = await repo.DeleteUserDataAsync(fromUserId, ct);
        return new UserDataTransfer(UserDataDeletionSubService.ServiceName,
            new Dictionary<string, long> { ["plays"] = moved, ["artistsDeleted"] = artists });
    }
}
