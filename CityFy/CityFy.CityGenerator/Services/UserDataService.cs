using CityFy.CityGenerator.SubServices;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Services;

public sealed class UserDataService(IGeneratedCityStoreSubService store, ILogger<UserDataService> logger) : IUserDataService
{
    public const string ServiceName = "CityGenerator";

    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var cities = await store.DeleteAllAsync(userId, ct);
        logger.LogInformation("Data of user {UserId} deleted: {Cities} generated cities", userId, cities);
        return new UserDataDeletion(ServiceName, new Dictionary<string, long> { ["generatedCities"] = cities });
    }
}
