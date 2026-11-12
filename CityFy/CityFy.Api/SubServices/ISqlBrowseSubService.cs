using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

/// <summary>Read-only access to the SQL Server database 'musicbrainz' (admin panel). Values are not redacted.</summary>
public interface ISqlBrowseSubService
{
    /// <summary>Tables with their row counts (from the catalog: fast even on large tables).</summary>
    Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct);

    /// <summary>
    /// The name is used only after matching it against the real tables (no SQL built from input).
    /// Throws NOT_FOUND (404) for an unknown table.
    /// </summary>
    Task<TablePage> GetTablePageAsync(string name, int page, int pageSize, CancellationToken ct);
}
