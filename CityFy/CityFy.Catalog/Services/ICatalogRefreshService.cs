using CityFy.Catalog.Models;
using ServiceDefault.Contracts;

namespace CityFy.Catalog.Services;

/// <summary>Pipeline side: the user's artist catalog (artist -> genre and subgenre).</summary>
public interface ICatalogRefreshService
{
    /// <summary>
    /// Rebuilds the user's artist catalog from all the plays (MusicBrainz resolve + genre hierarchy).
    /// Null when the user has no plays (nothing is stored).
    /// </summary>
    Task<UserCatalog?> RefreshAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Refreshes the catalog, then answers UserCatalogUpdated, or CatalogRefreshFailed with
    /// CITY_NO_PLAYS when the user has no plays (e.g. data deleted while the run was queued).
    /// </summary>
    Task HandleRefreshRequestedAsync(RefreshCatalogRequested message, CancellationToken ct);

    /// <summary>Last attempt failed: publishes CatalogRefreshFailed with the error code.</summary>
    Task HandleRefreshFailedAsync(RefreshCatalogRequested message, Exception error, CancellationToken ct);
}
