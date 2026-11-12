using ServiceDefault.City;

namespace CityFy.Api.Clients;

/// <summary>Elaboration: cities, plus every play and city of a user on data deletion.</summary>
public interface IElaborationClient : IUserDataOwner
{
    /// <summary>Throws CITY_NOT_READY (404) when the city of that period was never built.</summary>
    Task<CityModel> GetCityAsync(string userId, string period, CancellationToken ct);
}
