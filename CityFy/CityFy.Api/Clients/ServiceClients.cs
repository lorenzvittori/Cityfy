using System.Net.Http.Json;
using ServiceDefault.City;

namespace CityFy.Api.Clients;

public sealed class ElaborationClient(HttpClient http) : DownstreamClient(http, "Elaboration")
{
    /// <summary>Throws CITY_NOT_READY (404) when the city of that period was never built.</summary>
    public Task<CityModel> GetCityAsync(string userId, string period, CancellationToken ct) =>
        SendAsync<CityModel>(new HttpRequestMessage(HttpMethod.Get,
            $"api/elaboration/city/{Uri.EscapeDataString(userId)}?period={Uri.EscapeDataString(period)}"), ct);
}

public sealed class OrchestratorClient(HttpClient http) : DownstreamClient(http, "Orchestrator")
{
    public Task<List<PipelineRunInfo>> ListRunsAsync(string userId, int limit, CancellationToken ct) =>
        SendAsync<List<PipelineRunInfo>>(new HttpRequestMessage(HttpMethod.Get,
            $"api/pipeline/runs?userId={Uri.EscapeDataString(userId)}&limit={limit}"), ct);
}

public sealed class SpotifyServiceClient(HttpClient http) : DownstreamClient(http, "Retrieve.Spotify")
{
    private sealed record AuthorizeUrl(string Url);

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
}

/// <summary>Registered without the standard resilience handler: uploads are large and must not be retried/time out at 30s.</summary>
public sealed class DumpClient(HttpClient http) : DownstreamClient(http, "Retrieve.Dump.Spotify")
{
    public Task<UploadAccepted> UploadAsync(Stream zip, string fileName, string userId, CancellationToken ct)
    {
        var form = new MultipartFormDataContent
        {
            { new StreamContent(zip), "File", fileName },
            { new StringContent(userId), "UserId" }
        };
        return SendAsync<UploadAccepted>(new HttpRequestMessage(HttpMethod.Post, "debug/upload/zip") { Content = form }, ct);
    }

    public Task<TaskInfo> GetUploadAsync(string uploadId, CancellationToken ct) =>
        SendAsync<TaskInfo>(new HttpRequestMessage(HttpMethod.Get, $"api/upload/tasks/{Uri.EscapeDataString(uploadId)}"), ct);
}
