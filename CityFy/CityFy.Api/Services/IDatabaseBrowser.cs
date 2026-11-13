using CityFy.Api.Models;

namespace CityFy.Api.Services;

/// <summary>Read-only view of the databases for the admin panel (secrets redacted).</summary>
public interface IDatabaseBrowser
{
    Task<IReadOnlyList<TableInfo>> ListCollectionsAsync(CancellationToken ct);

    /// <summary>Throws NOT_FOUND (404) for an unknown collection.</summary>
    Task<TablePage> GetCollectionPageAsync(string name, int page, int pageSize, CancellationToken ct);

    Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct);

    /// <summary>Throws NOT_FOUND (404) for an unknown table.</summary>
    Task<TablePage> GetTablePageAsync(string name, int page, int pageSize, CancellationToken ct);
}
