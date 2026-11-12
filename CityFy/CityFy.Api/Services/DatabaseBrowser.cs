using CityFy.Api.Models;
using CityFy.Api.SubServices;

namespace CityFy.Api.Services;

/// <summary>
/// Read-only view of the databases for the admin panel: every page is redacted (fields that look
/// like secrets) before it leaves the API.
/// </summary>
public sealed class DatabaseBrowser(IMongoBrowseSubService mongo, ISqlBrowseSubService sql, IRedactionSubService redaction) : IDatabaseBrowser
{
    public Task<IReadOnlyList<TableInfo>> ListCollectionsAsync(CancellationToken ct) => mongo.ListCollectionsAsync(ct);

    public async Task<TablePage> GetCollectionPageAsync(string name, int page, int pageSize, CancellationToken ct)
    {
        var result = await mongo.GetCollectionPageAsync(name, page, pageSize, ct);
        redaction.Redact(result.Rows);
        return result;
    }

    public Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct) => sql.ListTablesAsync(ct);

    public async Task<TablePage> GetTablePageAsync(string name, int page, int pageSize, CancellationToken ct)
    {
        var result = await sql.GetTablePageAsync(name, page, pageSize, ct);
        redaction.Redact(result.Rows);
        return result;
    }
}
