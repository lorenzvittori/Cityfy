using ServiceDefault.City;

namespace CityFy.Elaboration.Services;

public interface ICityService
{
    /// <summary>Aggregates the user's plays, resolves the artists' genres and stores the city.</summary>
    Task<CityModel> BuildAsync(string userId, string period, CancellationToken ct);

    Task<CityModel?> GetAsync(string userId, string period, CancellationToken ct);
}
