using CityFy.Elaboration.Repositories;
using ServiceDefault.Models;

namespace CityFy.Elaboration.Services;

public sealed class UserDataService(IElaborationRepository repo, ILogger<UserDataService> logger) : IUserDataService
{
    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var (plays, cities) = await repo.DeleteUserDataAsync(userId, ct);
        logger.LogInformation("Data of user {UserId} deleted: {Plays} plays, {Cities} cities", userId, plays, cities);
        return new UserDataDeletion("Elaboration", new Dictionary<string, long> { ["plays"] = plays, ["cities"] = cities });
    }
}
