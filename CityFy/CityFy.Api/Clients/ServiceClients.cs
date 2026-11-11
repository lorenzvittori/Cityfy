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

    /// <summary>Admin: runs of every user, most recent first.</summary>
    public Task<List<PipelineRunInfo>> ListAllRunsAsync(int limit, CancellationToken ct) =>
        SendAsync<List<PipelineRunInfo>>(new HttpRequestMessage(HttpMethod.Get, $"api/pipeline/runs?limit={limit}"), ct);

    public Task<System.Text.Json.JsonElement> CancelRunAsync(string runId, CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"api/pipeline/runs/{Uri.EscapeDataString(runId)}/cancel"), ct);

    /// <summary>Admin: rebuild a user's city now (no new import needed).</summary>
    public Task<PipelineRunInfo> TriggerAsync(string userId, CancellationToken ct) =>
        SendAsync<PipelineRunInfo>(new HttpRequestMessage(HttpMethod.Post, $"debug/pipeline/trigger/{Uri.EscapeDataString(userId)}"), ct);
}

/// <summary>Admin: MusicBrainz catalog import (download + import of the dump, minutes to hours).</summary>
public sealed class MusicBrainzServiceClient(HttpClient http) : DownstreamClient(http, "Retrieve.MusicBrainz")
{
    public Task<System.Text.Json.JsonElement> GetImportStatusAsync(CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Get, "api/retrieve/status"), ct);

    /// <summary>Throws PROCESS_NOT_CANCELLABLE (409) when no import is running.</summary>
    public Task<System.Text.Json.JsonElement> CancelImportAsync(CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Post, "api/retrieve/cancel"), ct);

    /// <summary>Throws MB_RETRIEVE_ALREADY_RUNNING (409) when an import is queued or running.</summary>
    public Task<System.Text.Json.JsonElement> StartImportAsync(CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Post, "api/retrieve"), ct);
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

    /// <summary>Admin: most recent retrieve jobs of every user, with progress (passed through as is).</summary>
    public Task<System.Text.Json.JsonElement> ListJobsAsync(int limit, CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Get, $"internal/spotify/jobs?limit={limit}"), ct);

    public Task<System.Text.Json.JsonElement> CancelJobAsync(string jobId, CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"internal/spotify/jobs/{Uri.EscapeDataString(jobId)}/cancel"), ct);

    /// <summary>Admin: every linked account (no tokens).</summary>
    public Task<List<SpotifyAccountInfo>> ListAccountsAsync(CancellationToken ct) =>
        SendAsync<List<SpotifyAccountInfo>>(new HttpRequestMessage(HttpMethod.Get, "internal/spotify/accounts"), ct);
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

    public Task<System.Text.Json.JsonElement> CancelUploadAsync(string uploadId, CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"debug/upload/tasks/{Uri.EscapeDataString(uploadId)}/cancel"), ct);

    /// <summary>Admin: uploads of every user (paged, most recent first), with progress (passed through as is).</summary>
    public Task<System.Text.Json.JsonElement> ListUploadsAsync(int page, int pageSize, CancellationToken ct) =>
        SendAsync<System.Text.Json.JsonElement>(new HttpRequestMessage(HttpMethod.Get, $"debug/upload/tasks?page={page}&pageSize={pageSize}"), ct);
}
