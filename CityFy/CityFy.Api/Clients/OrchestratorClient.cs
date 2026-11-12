using ServiceDefault.Clients;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class OrchestratorClient(HttpClient http) : DownstreamClient(http, "Orchestrator"), IOrchestratorClient
{
    /// <summary>Every pipeline run of the user (Pending ones never start).</summary>
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public Task<List<PipelineRunInfo>> ListRunsAsync(string userId, int limit, CancellationToken ct) =>
        SendAsync<List<PipelineRunInfo>>(new HttpRequestMessage(HttpMethod.Get,
            $"api/pipeline/runs?userId={Uri.EscapeDataString(userId)}&limit={limit}"), ct);

    public Task<List<PipelineRunInfo>> ListAllRunsAsync(int limit, CancellationToken ct) =>
        SendAsync<List<PipelineRunInfo>>(new HttpRequestMessage(HttpMethod.Get, $"api/pipeline/runs?limit={limit}"), ct);

    public Task<JsonElement> CancelRunAsync(string runId, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"api/pipeline/runs/{Uri.EscapeDataString(runId)}/cancel"), ct);

    public Task<PipelineRunInfo> TriggerAsync(string userId, CancellationToken ct) =>
        SendAsync<PipelineRunInfo>(new HttpRequestMessage(HttpMethod.Post, $"internal/pipeline/trigger/{Uri.EscapeDataString(userId)}"), ct);
}
