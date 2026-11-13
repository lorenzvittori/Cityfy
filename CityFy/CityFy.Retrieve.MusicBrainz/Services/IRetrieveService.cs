using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IRetrieveService
{
    /// <summary>Downloads the latest export, extracts the needed tables and imports them.</summary>
    Task<RetrieveResult> RunRetrieveAsync(CancellationToken cancellationToken = default);

    /// <summary>Publishes CatalogUpdated (every city is rebuilt). A failure here does not fail the import.</summary>
    Task NotifyCatalogUpdatedAsync(RetrieveResult result, CancellationToken ct);
}