using System.Net.Http.Json;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class SpotifyServiceClient(HttpClient http) : DownstreamClient(http, "Retrieve.Spotify"), ISpotifyServiceClient
{
    private sealed record AuthorizeUrl(string Url);

    /// <summary>Stops the user's retrieves, deletes the jobs and the linked account (refresh token).</summary>
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/spotify/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public async Task<string> GetAuthorizeUrlAsync(CancellationToken ct) =>
        (await SendAsync<AuthorizeUrl>(new HttpRequestMessage(HttpMethod.Get, "internal/spotify/authorize-url"), ct)).Url;

    public Task<SpotifyLinkResult> LinkAsync(string code, string? state, CancellationToken ct) =>
        SendAsync<SpotifyLinkResult>(new HttpRequestMessage(HttpMethod.Post, "internal/spotify/link")
        {
            Content = JsonContent.Create(new { code, state }, options: JsonOptions)
        }, ct);

    public Task<SpotifyAccountInfo> GetAccountAsync(string userId, CancellationToken ct) =>
        SendAsync<SpotifyAccountInfo>(new HttpRequestMessage(HttpMethod.Get, $"internal/spotify/accounts/{Uri.EscapeDataString(userId)}"), ct);

    public Task<JobAccepted> StartRetrieveAsync(string userId, CancellationToken ct) =>
        SendAsync<JobAccepted>(new HttpRequestMessage(HttpMethod.Post, $"internal/spotify/accounts/{Uri.EscapeDataString(userId)}/retrieve"), ct);

    public Task<SpotifyJobInfo> GetJobAsync(string jobId, CancellationToken ct) =>
        SendAsync<SpotifyJobInfo>(new HttpRequestMessage(HttpMethod.Get, $"internal/spotify/jobs/{Uri.EscapeDataString(jobId)}"), ct);

    public Task<JsonElement> ListJobsAsync(int limit, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Get, $"internal/spotify/jobs?limit={limit}"), ct);

    public Task<JsonElement> CancelJobAsync(string jobId, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"internal/spotify/jobs/{Uri.EscapeDataString(jobId)}/cancel"), ct);

    public Task<List<SpotifyAccountInfo>> ListAccountsAsync(CancellationToken ct) =>
        SendAsync<List<SpotifyAccountInfo>>(new HttpRequestMessage(HttpMethod.Get, "internal/spotify/accounts"), ct);
}
