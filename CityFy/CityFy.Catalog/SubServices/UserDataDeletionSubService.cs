using CityFy.Catalog.Repositories;
using ServiceDefault.Models;

namespace CityFy.Catalog.SubServices;

public sealed class UserDataDeletionSubService(ICatalogRepository repo) : IUserDataDeletionSubService
{
    public const string ServiceName = "Catalog";

    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var (plays, artists) = await repo.DeleteUserDataAsync(userId, ct);
        return new UserDataDeletion(ServiceName, new Dictionary<string, long> { ["plays"] = plays, ["artists"] = artists });
    }
}
