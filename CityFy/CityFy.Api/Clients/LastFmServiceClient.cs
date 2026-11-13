using System.Net.Http.Json;
using System.Text.Json;
using ServiceDefault.Clients;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class LastFmServiceClient(HttpClient http) : DownstreamClient(http, "Retrieve.LastFm"), ILastFmServiceClient
{
    private sealed record AuthorizeUrl(string Url);

    /// <summary>Stops the user's imports, deletes the imports and the linked account (session key).</summary>
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/lastfm/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public Task<UserDataDeletion> DeleteEverythingAsync(CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, "internal/lastfm/data"), ct);

    public async Task<string> GetAuthorizeUrlAsync(CancellationToken ct) =>
        (await SendAsync<AuthorizeUrl>(new HttpRequestMessage(HttpMethod.Get, "internal/lastfm/authorize-url"), ct)).Url;

    public Task<LastFmLinkResult> LinkAsync(string newUserId, string token, CancellationToken ct) =>
        SendAsync<LastFmLinkResult>(new HttpRequestMessage(HttpMethod.Post, "internal/lastfm/link")
        {
            Content = JsonContent.Create(new { userId = newUserId, token }, options: JsonOptions)
        }, ct);

    public Task<LastFmAccountInfo> GetAccountAsync(string userId, CancellationToken ct) =>
        SendAsync<LastFmAccountInfo>(new HttpRequestMessage(HttpMethod.Get, $"internal/lastfm/accounts/{Uri.EscapeDataString(userId)}"), ct);

    public Task<LastFmImportInfo> GetImportAsync(string importId, CancellationToken ct) =>
        SendAsync<LastFmImportInfo>(new HttpRequestMessage(HttpMethod.Get, $"internal/lastfm/imports/{Uri.EscapeDataString(importId)}"), ct);

    public Task<JsonElement> ListAccountsAsync(CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Get, "internal/lastfm/accounts"), ct);

    public Task<JsonElement> ListImportsAsync(int limit, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Get, $"internal/lastfm/imports?limit={limit}"), ct);

    public Task<JsonElement> StartImportAsync(string userId, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"internal/lastfm/accounts/{Uri.EscapeDataString(userId)}/import"), ct);

    public Task PollNowAsync(string userId, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, $"internal/lastfm/accounts/{Uri.EscapeDataString(userId)}/poll"), ct);

    public Task<JsonElement> CancelImportAsync(string importId, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"internal/lastfm/imports/{Uri.EscapeDataString(importId)}/cancel"), ct);
}
