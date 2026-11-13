using System.Net.Http.Json;
using ServiceDefault.City;
using ServiceDefault.Clients;

namespace CityFy.CityGenerator.Clients;

public sealed class CatalogClient(HttpClient http) : DownstreamClient(http, "Catalog"), ICatalogClient
{
    public Task<UserCatalogVersion> GetVersionAsync(string userId, CancellationToken ct) =>
        SendAsync<UserCatalogVersion>(new HttpRequestMessage(HttpMethod.Get, $"api/catalog/version/{Uri.EscapeDataString(userId)}"), ct);

    public Task<CityModelResult> GetCityModelAsync(string userId, CityModelQuery query, CancellationToken ct) =>
        SendAsync<CityModelResult>(new HttpRequestMessage(HttpMethod.Post, $"api/catalog/city-model/{Uri.EscapeDataString(userId)}")
        {
            Content = JsonContent.Create(query, options: JsonOptions)
        }, ct);
}
