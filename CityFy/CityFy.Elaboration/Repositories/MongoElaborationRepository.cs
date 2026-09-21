using CityFy.Elaboration.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Elaboration.Repositories;

public class MongoElaborationRepository : IElaborationRepository
{
    private readonly IMongoDatabase _db;

    public MongoElaborationRepository(MongoOptions options)
    {
        var client = new MongoClient(options.ConnectionString);
        _db = client.GetDatabase(options.Database);
    }

    public async Task<IEnumerable<TagGraph>> GetTagGraphsAsync(string? seedTag = null)
    {
        var col = _db.GetCollection<BsonDocument>("musicbrainz_tag_graphs");
        FilterDefinition<BsonDocument> filter = FilterDefinition<BsonDocument>.Empty;
        if (!string.IsNullOrWhiteSpace(seedTag))
            filter = Builders<BsonDocument>.Filter.Eq("SeedTag", seedTag);

        var docs = await col.Find(filter).ToListAsync();
        var list = docs.Select(d => JsonSerializer.Deserialize<TagGraph>(d.ToJson()));
        return list.Where(x => x != null)!.Select(x => x!);
    }

    public async Task InsertGraphAsync(Graph graph)
    {
        var col = _db.GetCollection<BsonDocument>("musicbrainz_tag_graphs_bl");
        var doc = BsonDocument.Parse(JsonSerializer.Serialize(graph));
        await col.InsertOneAsync(doc);
    }
}
