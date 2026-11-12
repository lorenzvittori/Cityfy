using System.Data;
using System.Text.Json.Nodes;
using CityFy.Api.Models;
using Microsoft.Data.SqlClient;
using ServiceDefault.Errors;

namespace CityFy.Api.SubServices;

public sealed class SqlBrowseSubService(SqlConnection sql) : ISqlBrowseSubService
{
    public const int MaxPageSize = 200;

    private const string ListTablesQuery = """
        SELECT s.name + '.' + t.name, SUM(p.rows)
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
        GROUP BY s.name, t.name
        ORDER BY s.name, t.name
        """;

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        await using var cmd = new SqlCommand(ListTablesQuery, sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<TableInfo>();
        while (await reader.ReadAsync(ct))
            result.Add(new TableInfo(reader.GetString(0), reader.GetInt64(1)));
        return result;
    }

    public async Task<TablePage> GetTablePageAsync(string name, int page, int pageSize, CancellationToken ct)
    {
        var table = (await ListTablesAsync(ct)).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new CityFyException(ErrorCodes.NotFound, $"Table '{name}' not found", StatusCodes.Status404NotFound);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var parts = table.Name.Split('.', 2);
        // Identifiers come from the catalog and are quoted; paging values are parameters
        var query = $"SELECT * FROM {Quote(parts[0])}.{Quote(parts[1])} ORDER BY (SELECT NULL) OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

        await using var cmd = new SqlCommand(query, sql);
        cmd.Parameters.AddWithValue("@skip", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@take", pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new JsonArray();
        while (await reader.ReadAsync(ct))
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : JsonValue.Create(reader.GetValue(i).ToString());
            rows.Add(row);
        }

        return new TablePage(table.Name, table.Rows, page, pageSize, columns, rows);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (sql.State != ConnectionState.Open)
            await sql.OpenAsync(ct);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
}
