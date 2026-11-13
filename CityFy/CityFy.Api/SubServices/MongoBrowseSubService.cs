using System.Text.Json.Nodes;
using CityFy.Api.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using ServiceDefault.Errors;

namespace CityFy.Api.SubServices;

public sealed class MongoBrowseSubService(IMongoDatabase mongo) : IMongoBrowseSubService
{
    public const int MaxPageSize = 200;

    private static readonly JsonWriterSettings JsonSettings = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };

    public async Task<IReadOnlyList<TableInfo>> ListCollectionsAsync(CancellationToken ct)
    {
        var names = await (await mongo.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        var result = new List<TableInfo>();
        foreach (var name in names.Order(StringComparer.Ordinal))
            result.Add(new TableInfo(name, await mongo.GetCollection<BsonDocument>(name).EstimatedDocumentCountAsync(cancellationToken: ct)));
        return result;
    }

    public async Task<TablePage> GetCollectionPageAsync(string name, int page, int pageSize, CancellationToken ct)
    {
        var names = await (await mongo.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (!names.Contains(name, StringComparer.Ordinal))
            throw new CityFyException(ErrorCodes.NotFound, $"Collection '{name}' not found", StatusCodes.Status404NotFound);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var collection = mongo.GetCollection<BsonDocument>(name);
        var total = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct);
        var docs = await collection.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("_id"))
            .Skip((page - 1) * pageSize).Limit(pageSize)
            .ToListAsync(ct);

        var rows = new JsonArray(docs.Select(ToJsonNode).ToArray());
        var columns = docs.SelectMany(d => d.Names).Distinct().ToList();
        return new TablePage(name, total, page, pageSize, columns, rows);
    }

    /// <summary>Relaxed extended JSON (dates and ids readable), as a node of the page.</summary>
    private static JsonNode? ToJsonNode(BsonDocument document) => JsonNode.Parse(document.ToJson(JsonSettings));
}
