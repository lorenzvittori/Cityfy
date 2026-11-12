namespace CityFy.Elaboration.SubServices;

/// <summary>Answers to BuildCityRequested (consumed by the orchestrator).</summary>
public interface ICityBuildEventsSubService
{
    Task PublishBuiltAsync(string runId, string userId, IReadOnlyList<string> periods, CancellationToken ct);

    Task PublishFailedAsync(string runId, string userId, string errorCode, string error, CancellationToken ct);
}
