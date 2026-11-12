using ServiceDefault.City;

namespace CityFy.Elaboration.SubServices;

/// <summary>Built cities ('city_models').</summary>
public interface ICityStoreSubService
{
    Task SaveAsync(CityModel city, CancellationToken ct);

    Task<CityModel?> GetAsync(string userId, string period, CancellationToken ct);
}
