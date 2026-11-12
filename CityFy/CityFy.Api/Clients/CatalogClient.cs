using ServiceDefault.Clients;
using System.Globalization;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class CatalogClient(HttpClient http) : DownstreamClient(http, "Catalog"), ICatalogClient
{
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public async Task<IReadOnlyList<GenreOption>> GetGenresAsync(string userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = string.Join('&', new[] { ("from", from), ("to", to) }
            .Where(p => p.Item2.HasValue)
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2!.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}"));
        return await SendAsync<List<GenreOption>>(new HttpRequestMessage(HttpMethod.Get,
            $"api/catalog/genres/{Uri.EscapeDataString(userId)}{(query.Length > 0 ? "?" + query : "")}"), ct);
    }
}
