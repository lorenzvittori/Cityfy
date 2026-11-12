using System.Text.Json;

namespace CityFy.Api.Clients;

/// <summary>Orchestrator: pipeline runs (city builds).</summary>
public interface IOrchestratorClient : IUserDataOwner
{
    Task<List<PipelineRunInfo>> ListRunsAsync(string userId, int limit, CancellationToken ct);

    /// <summary>Admin: runs of every user, most recent first.</summary>
    Task<List<PipelineRunInfo>> ListAllRunsAsync(int limit, CancellationToken ct);

    /// <summary>Throws PIPELINE_RUN_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409).</summary>
    Task<JsonElement> CancelRunAsync(string runId, CancellationToken ct);

    /// <summary>Rebuild a user's city now (no new import needed).</summary>
    Task<PipelineRunInfo> TriggerAsync(string userId, CancellationToken ct);
}
