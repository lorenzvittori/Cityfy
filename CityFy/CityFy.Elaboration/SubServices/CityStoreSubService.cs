using CityFy.Elaboration.Repositories;
using ServiceDefault.City;

namespace CityFy.Elaboration.SubServices;

public sealed class CityStoreSubService(IElaborationRepository repo) : ICityStoreSubService
{
    public Task SaveAsync(CityModel city, CancellationToken ct) => repo.SaveCityAsync(city, ct);

    public Task<CityModel?> GetAsync(string userId, string period, CancellationToken ct) => repo.GetCityAsync(userId, period, ct);
}
