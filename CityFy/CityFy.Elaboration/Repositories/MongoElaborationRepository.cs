using CityFy.Elaboration.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Elaboration.Repositories;

public class MongoElaborationRepository : IElaborationRepository
{
    private readonly IMongoDatabase _db;
    private const string OutputCollection = "elaboration_graphs";

    public MongoElaborationRepository(MongoOptions options)
    {
        var client = new MongoClient(options.ConnectionString);
        _db = client.GetDatabase(options.Database);
    }

    public async Task<Graph?> GetGraphByIdAsync(string id)
    {
        var col = _db.GetCollection<BsonDocument>(OutputCollection);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = await col.Find(filter).FirstOrDefaultAsync();
        if (doc == null) return null;
        return JsonSerializer.Deserialize<Graph>(doc.ToJson());
    }

    public async Task<IEnumerable<Graph>> GetGraphsPagedAsync(int page, int pageSize)
    {
        var col = _db.GetCollection<BsonDocument>(OutputCollection);
        var skip = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
        var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).Skip(skip).Limit(Math.Max(1, pageSize)).ToListAsync();
        var list = docs.Select(d => JsonSerializer.Deserialize<Graph>(d.ToJson()));
        return list.Where(x => x != null)!.Select(x => x!);
    }

    public async Task InsertGraphAsync(Graph graph)
    {
        var col = _db.GetCollection<BsonDocument>(OutputCollection);
        var doc = BsonDocument.Parse(JsonSerializer.Serialize(graph));
        await col.InsertOneAsync(doc);
    }
}
