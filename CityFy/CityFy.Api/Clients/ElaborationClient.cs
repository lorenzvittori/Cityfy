using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class ElaborationClient(HttpClient http) : DownstreamClient(http, "Elaboration"), IElaborationClient
{
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public Task<CityModel> GetCityAsync(string userId, string period, CancellationToken ct) =>
        SendAsync<CityModel>(new HttpRequestMessage(HttpMethod.Get,
            $"api/elaboration/city/{Uri.EscapeDataString(userId)}?period={Uri.EscapeDataString(period)}"), ct);
}
