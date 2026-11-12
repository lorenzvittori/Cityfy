using CityFy.Orchestrator.Models;

namespace CityFy.Orchestrator.SubServices;

public interface IBuildRequestSubService
{
    /// <summary>Publishes BuildCityRequested for the run (consumed by Elaboration).</summary>
    Task PublishAsync(PipelineRun run, CancellationToken ct);
}
