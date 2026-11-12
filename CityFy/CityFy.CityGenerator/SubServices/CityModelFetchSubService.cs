using CityFy.CityGenerator.Clients;
using CityFy.CityGenerator.Models;
using ServiceDefault.City;

namespace CityFy.CityGenerator.SubServices;

public sealed class CityModelFetchSubService(ICatalogClient catalog) : ICityModelFetchSubService
{
    public async Task<string> GetCatalogVersionAsync(string userId, CancellationToken ct) =>
        (await catalog.GetVersionAsync(userId, ct)).Version;

    public Task<CityModelResult> FetchAsync(GeneratedCity city, CancellationToken ct) =>
        catalog.GetCityModelAsync(city.UserId, new CityModelQuery(city.From, city.To, city.Genres, city.Subgenres), ct);
}
