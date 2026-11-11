using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using ServiceDefault.Errors;

namespace CityFy.Api.Admin;

public sealed record TableInfo(string Name, long Rows);

public sealed record TablePage(string Name, long Total, int Page, int PageSize, IReadOnlyList<string> Columns, JsonArray Rows);

/// <summary>
/// Read-only view of the databases for the admin panel. Names coming from the request are only
/// used after being matched against the real list of collections/tables (no SQL built from input),
/// and fields that look like secrets are redacted.
/// </summary>
public sealed class DatabaseBrowser
{
    public const int MaxPageSize = 200;
    private const string Redacted = "«redacted»";
    private static readonly string[] SecretMarkers = ["token", "secret", "password", "pwd"];

    private readonly IMongoDatabase _mongo;
    private readonly SqlConnection _sql;

    public DatabaseBrowser(IMongoDatabase mongo, SqlConnection sql)
    {
        _mongo = mongo;
        _sql = sql;
    }

    // --- Mongo ------------------------------------------------------------------

    public async Task<IReadOnlyList<TableInfo>> ListCollectionsAsync(CancellationToken ct)
    {
        var names = await (await _mongo.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        var result = new List<TableInfo>();
        foreach (var name in names.Order(StringComparer.Ordinal))
            result.Add(new TableInfo(name, await _mongo.GetCollection<BsonDocument>(name).EstimatedDocumentCountAsync(cancellationToken: ct)));
        return result;
    }

    public async Task<TablePage> GetCollectionPageAsync(string name, int page, int pageSize, CancellationToken ct)
    {
        var names = await (await _mongo.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (!names.Contains(name, StringComparer.Ordinal))
            throw NotFound($"Collection '{name}'");

        (page, pageSize) = Normalize(page, pageSize);
        var collection = _mongo.GetCollection<BsonDocument>(name);
        var total = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct);
        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("_id"))
            .Skip((page - 1) * pageSize).Limit(pageSize)
            .ToListAsync(ct);

        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
        var rows = new JsonArray(docs.Select(d => Redact(JsonNode.Parse(d.ToJson(settings)))).ToArray());
        var columns = docs.SelectMany(d => d.Names).Distinct().ToList();
        return new TablePage(name, total, page, pageSize, columns, rows);
    }

    // --- SQL Server (MusicBrainz) -------------------------------------------------

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        // Row counts from the catalog (fast even on large tables)
        const string query = """
            SELECT s.name + '.' + t.name, SUM(p.rows)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            GROUP BY s.name, t.name
            ORDER BY s.name, t.name
            """;
        await using var cmd = new SqlCommand(query, _sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<TableInfo>();
        while (await reader.ReadAsync(ct))
            result.Add(new TableInfo(reader.GetString(0), reader.GetInt64(1)));
        return result;
    }

    public async Task<TablePage> GetTablePageAsync(string name, int page, int pageSize, CancellationToken ct)
    {
        var table = (await ListTablesAsync(ct)).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw NotFound($"Table '{name}'");

        (page, pageSize) = Normalize(page, pageSize);
        var parts = table.Name.Split('.', 2);
        // Identifiers come from the catalog and are quoted; paging values are parameters
        var sql = $"SELECT * FROM {Quote(parts[0])}.{Quote(parts[1])} ORDER BY (SELECT NULL) OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

        await using var cmd = new SqlCommand(sql, _sql);
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
            rows.Add(Redact(row));
        }

        return new TablePage(table.Name, table.Rows, page, pageSize, columns, rows);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_sql.State != System.Data.ConnectionState.Open)
            await _sql.OpenAsync(ct);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    private static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    private static CityFyException NotFound(string what) =>
        new(ErrorCodes.NotFound, $"{what} not found", StatusCodes.Status404NotFound);

    /// <summary>Replaces the values of fields whose name looks like a secret (recursively).</summary>
    internal static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (SecretMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase)))
                        obj[key] = Redacted;
                    else
                        Redact(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    Redact(item);
                break;
        }
        return node;
    }
}
