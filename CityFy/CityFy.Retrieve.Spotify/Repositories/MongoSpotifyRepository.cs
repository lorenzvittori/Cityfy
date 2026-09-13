using CityFy.RtrieveSpotify.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.RtrieveSpotify.Repositories
{
    public class MongoSpotifyRepository : ISpotifyRepository
    {
        private readonly IMongoDatabase _db;

        public MongoSpotifyRepository(MongoOptions options)
        {
            var client = new MongoClient(options.ConnectionString);
            _db = client.GetDatabase(options.Database);
        }

        public async Task InsertTracksAsync(IEnumerable<Track> tracks)
        {
            var col = _db.GetCollection<BsonDocument>("spotify_tracks");
            var docs = tracks.Select(t => BsonDocument.Parse(JsonSerializer.Serialize(t))).ToList();
            if (docs.Count > 0)
                await col.InsertManyAsync(docs);
        }

        public async Task InsertAlbumsAsync(IEnumerable<Album> albums)
        {
            var col = _db.GetCollection<BsonDocument>("spotify_albums");
            var docs = albums.Select(a => BsonDocument.Parse(JsonSerializer.Serialize(a))).ToList();
            if (docs.Count > 0)
                await col.InsertManyAsync(docs);
        }
    }
}
