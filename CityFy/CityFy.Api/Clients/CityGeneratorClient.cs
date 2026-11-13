using System.Net.Http.Json;
using ServiceDefault.City;
using ServiceDefault.Clients;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class CityGeneratorClient(HttpClient http) : DownstreamClient(http, "CityGenerator"), ICityGeneratorClient
{
    private static string Cities(string userId) => $"api/city-generator/{Uri.EscapeDataString(userId)}/cities";

    private static string City(string userId, string cityId) => $"{Cities(userId)}/{Uri.EscapeDataString(cityId)}";

    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public Task<GeneratedCityInfo> CreateAsync(string userId, GenerateCityRequest request, CancellationToken ct) =>
        SendAsync<GeneratedCityInfo>(new HttpRequestMessage(HttpMethod.Post, Cities(userId))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        }, ct);

    public Task<RecipeSchema> GetRecipeSchemaAsync(CancellationToken ct) =>
        SendAsync<RecipeSchema>(new HttpRequestMessage(HttpMethod.Get, "api/city-generator/recipe/schema"), ct);

    public Task<CityFormOptions> GetFormOptionsAsync(CancellationToken ct) =>
        SendAsync<CityFormOptions>(new HttpRequestMessage(HttpMethod.Get, "api/city-generator/options"), ct);

    public Task<GeneratedCityPage> ListAsync(string userId, int page, int pageSize, CancellationToken ct) =>
        SendAsync<GeneratedCityPage>(new HttpRequestMessage(HttpMethod.Get, $"{Cities(userId)}?page={page}&pageSize={pageSize}"), ct);

    public Task<GeneratedCityDetail> GetAsync(string userId, string cityId, CancellationToken ct) =>
        SendAsync<GeneratedCityDetail>(new HttpRequestMessage(HttpMethod.Get, City(userId, cityId)), ct);

    public Task DeleteAsync(string userId, string cityId, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, City(userId, cityId)), ct);

    public Task<GeneratedCityInfo> CancelAsync(string userId, string cityId, CancellationToken ct) =>
        SendAsync<GeneratedCityInfo>(new HttpRequestMessage(HttpMethod.Post, $"{City(userId, cityId)}/cancel"), ct);
}
