using ServiceDefault.Clients;
using System.Globalization;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class CatalogClient(HttpClient http) : DownstreamClient(http, "Catalog"), ICatalogClient
{
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/users/{Uri.EscapeDataString(userId)}/data"), ct);

    /// <summary>Every play changes owner; the old owner's artist catalog is deleted.</summary>
    public Task<UserDataTransfer> TransferUserDataAsync(string fromUserId, string toUserId, CancellationToken ct) =>
        SendAsync<UserDataTransfer>(new HttpRequestMessage(HttpMethod.Post, $"internal/users/{Uri.EscapeDataString(fromUserId)}/transfer")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new UserDataTransferRequest(toUserId), options: JsonOptions)
        }, ct);

    public async Task<IReadOnlyList<GenreOption>> GetGenresAsync(string userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = new List<string>();
        AddDate(query, "from", from);
        AddDate(query, "to", to);
        var queryString = query.Count > 0 ? "?" + string.Join('&', query) : "";
        return await SendAsync<List<GenreOption>>(new HttpRequestMessage(HttpMethod.Get,
            $"api/catalog/genres/{Uri.EscapeDataString(userId)}{queryString}"), ct);
    }

    /// <summary>"name=2026-01-01T00:00:00.0000000Z" (UTC, round-trip), only when the date is set.</summary>
    private static void AddDate(List<string> query, string name, DateTime? value)
    {
        if (value is { } date)
            query.Add($"{name}={Uri.EscapeDataString(date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}");
    }
}
