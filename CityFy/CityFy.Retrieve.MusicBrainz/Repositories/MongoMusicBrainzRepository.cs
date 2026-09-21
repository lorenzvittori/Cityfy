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
}
