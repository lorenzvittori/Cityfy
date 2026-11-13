using System.Text.Json.Nodes;
using CityFy.Api.Models;
using Npgsql;
using ServiceDefault.Errors;

namespace CityFy.Api.SubServices;

public sealed class SqlBrowseSubService(NpgsqlDataSource sql) : ISqlBrowseSubService
{
    public const int MaxPageSize = 200;

    // User tables with the row count estimated by PostgreSQL (reltuples: -1 before the first ANALYZE)
    private const string ListTablesQuery = """
        SELECT n.nspname || '.' || c.relname, GREATEST(c.reltuples, 0)::bigint
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p')
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
          AND n.nspname NOT LIKE 'pg_toast%'
        ORDER BY n.nspname, c.relname
        """;

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct)
    {
        await using var cmd = sql.CreateCommand(ListTablesQuery);
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
        var query = $"SELECT * FROM {Quote(parts[0])}.{Quote(parts[1])} OFFSET @skip LIMIT @take";

        await using var cmd = sql.CreateCommand(query);
        cmd.Parameters.AddWithValue("skip", (long)(page - 1) * pageSize);
        cmd.Parameters.AddWithValue("take", (long)pageSize);
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

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
