using CityFy.Orchestrator.Models;

namespace CityFy.Orchestrator.SubServices;

public interface ICatalogRefreshRequestSubService
{
    /// <summary>Publishes RefreshCatalogRequested for the run (consumed by Catalog).</summary>
    Task PublishAsync(PipelineRun run, CancellationToken ct);
}