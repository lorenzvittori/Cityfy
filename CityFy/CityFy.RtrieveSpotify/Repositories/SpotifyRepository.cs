using CityFy.RtrieveSpotify.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace CityFy.RtrieveSpotify.Repositories
{
    public class SpotifyRepository : ISpotifyRepository
    {
        private readonly IMongoDatabase _db;

        public SpotifyRepository(IConfiguration config)
        {
            var mongoConnection = config.GetValue<string>("Mongo:ConnectionString") ?? "mongodb://localhost:27017";
            var databaseName = config.GetValue<string>("Mongo:Database") ?? "cityfy_spotify";

            var client = new MongoClient(mongoConnection);
            _db = client.GetDatabase(databaseName);
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
