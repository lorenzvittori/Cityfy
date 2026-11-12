using CityFy.Elaboration.Repositories;
using ServiceDefault.Models;

namespace CityFy.Elaboration.SubServices;

public sealed class UserDataDeletionSubService(IElaborationRepository repo) : IUserDataDeletionSubService
{
    public const string ServiceName = "Elaboration";

    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var (plays, cities) = await repo.DeleteUserDataAsync(userId, ct);
        return new UserDataDeletion(ServiceName, new Dictionary<string, long> { ["plays"] = plays, ["cities"] = cities });
    }
}
