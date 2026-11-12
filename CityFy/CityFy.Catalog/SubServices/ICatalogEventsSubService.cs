using CityFy.Catalog.Models;

namespace CityFy.Catalog.SubServices;

/// <summary>Answers to RefreshCatalogRequested (orchestrator) and catalog updates (city generator).</summary>
public interface ICatalogEventsSubService
{
    /// <summary>UserCatalogUpdated: closes the run; cities generated with an older catalog version are outdated.</summary>
    Task PublishUpdatedAsync(string? runId, UserCatalog catalog, CancellationToken ct);

    Task PublishFailedAsync(string runId, string userId, string errorCode, string error, CancellationToken ct);
}
