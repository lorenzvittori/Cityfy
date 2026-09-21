using CityFy.Retrieve.MusicBrainz.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Retrieve.MusicBrainz.Repositories;

public class MongoMusicBrainzRepository : IMusicBrainzRepository
{
    private readonly IMongoDatabase _db;

    public MongoMusicBrainzRepository(MongoOptions options)
    {
        var client = new MongoClient(options.ConnectionString);
        _db = client.GetDatabase(options.Database);
    }

    public async Task InsertArtistTagsAsync(ArtistTags artistTags)
    {
        var col = _db.GetCollection<BsonDocument>("musicbrainz_artist_tags");
        var doc = BsonDocument.Parse(JsonSerializer.Serialize(artistTags));
        await col.InsertOneAsync(doc);
    }

    public async Task InsertRelatedTagsAsync(TagGraph graph)
    {
        var col = _db.GetCollection<BsonDocument>("musicbrainz_tag_graphs");
        var doc = BsonDocument.Parse(JsonSerializer.Serialize(graph));
        await col.InsertOneAsync(doc);
    }

    public async Task<TagGraph?> GetTagGraphByIdAsync(string id)
    {
        var col = _db.GetCollection<BsonDocument>("musicbrainz_tag_graphs");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = await col.Find(filter).FirstOrDefaultAsync();
        if (doc == null) return null;
        return JsonSerializer.Deserialize<TagGraph>(doc.ToJson());
    }

    public async Task<IEnumerable<TagGraph>> GetTagGraphsPagedAsync(int page, int pageSize)
    {
        var col = _db.GetCollection<BsonDocument>("musicbrainz_tag_graphs");
        var skip = (Math.Max(1, page) - 1) * Math.Max(1, pageSize);
        var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).Skip(skip).Limit(Math.Max(1, pageSize)).ToListAsync();
        var list = docs.Select(d => JsonSerializer.Deserialize<TagGraph>(d.ToJson()));
        return list.Where(x => x != null)!.Select(x => x!);
    }
}
