namespace CityFy.Retrieve.MusicBrainz.SubServices;

public interface ICatalogEventsSubService
{
    /// <summary>CatalogUpdated: the orchestrator rebuilds every city with the new catalog.</summary>
    Task PublishUpdatedAsync(string? dumpName, CancellationToken ct);
}