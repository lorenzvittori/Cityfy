using ServiceDefault.City;
using CityFy.Catalog.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ServiceDefault.Listening;

namespace CityFy.Catalog.Repositories;

public sealed class MongoCatalogRepository : ICatalogRepository
{
    /// <summary>Period cities of the first versions: no longer written, still erased with the user's data.</summary>
    private const string LegacyCityCollection = "city_models";
    private const string CatalogCollection = "user_catalogs";
    private const string PlacementCollection = "user_artist_placements";

    private readonly IMongoCollection<BsonDocument> _events;
    private readonly IMongoCollection<BsonDocument> _legacyCities;
    private readonly IMongoCollection<UserCatalog> _catalogs;
    private readonly IMongoCollection<ArtistPlacement> _placements;
    // Created once, on first use
    private readonly Lazy<Task> _indexes;

    public MongoCatalogRepository(IMongoDatabase db)
    {
        _events = db.GetCollection<BsonDocument>(ListeningEvent.CollectionName);
        _legacyCities = db.GetCollection<BsonDocument>(LegacyCityCollection);
        _catalogs = db.GetCollection<UserCatalog>(CatalogCollection);
        _placements = db.GetCollection<ArtistPlacement>(PlacementCollection);
        _indexes = new Lazy<Task>(() => _placements.Indexes.CreateOneAsync(new CreateIndexModel<ArtistPlacement>(
            Builders<ArtistPlacement>.IndexKeys.Ascending(p => p.UserId).Ascending(p => p.CatalogVersion))));
    }

    public async Task<List<ArtistPlays>> AggregateArtistPlaysAsync(string userId, DateTime? since, DateTime? until, int qualifiedPlayMs, CancellationToken ct)
    {
        var match = new BsonDocument
        {
            { nameof(ListeningEvent.UserId), userId },
            { nameof(ListeningEvent.ArtistName), new BsonDocument("$ne", BsonNull.Value) }
        };
        var playedAt = new BsonDocument();
        if (since.HasValue) playedAt.Add("$gte", since.Value);
        if (until.HasValue) playedAt.Add("$lt", until.Value);
        if (playedAt.ElementCount > 0)
            match.Add(nameof(ListeningEvent.PlayedAt), playedAt);

        // -1 marks "duration unknown" (API plays): it counts as qualified
        var msOrUnknown = new BsonDocument("$ifNull", new BsonArray { "$MsPlayed", -1 });

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$ArtistName" },
                { "ArtistUri", new BsonDocument("$max", "$ArtistUri") },
                { "Plays", new BsonDocument("$sum", 1L) },
                { "QualifiedPlays", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$or", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { msOrUnknown, -1 }),
                            new BsonDocument("$gte", new BsonArray { msOrUnknown, qualifiedPlayMs })
                        }),
                        1L,
                        0L
                    })) },
                { "MsPlayed", new BsonDocument("$sum", new BsonDocument("$toLong",
                    new BsonDocument("$ifNull", new BsonArray { "$MsPlayed", 0 }))) }
            })
        };

        var rows = await _events.Aggregate<BsonDocument>(pipeline, new AggregateOptions { AllowDiskUse = true }, ct).ToListAsync(ct);
        return rows.Select(r => BsonSerializer.Deserialize<ArtistPlays>(r)).ToList();
    }


    public async Task<bool> HasPlaysAsync(string userId, CancellationToken ct) =>
        await _events.Find(new BsonDocument(nameof(ListeningEvent.UserId), userId)).Limit(1).AnyAsync(ct);

    public async Task<UserCatalog?> GetUserCatalogAsync(string userId, CancellationToken ct) =>
        await _catalogs.Find(c => c.UserId == userId).FirstOrDefaultAsync(ct);

    public async Task<List<ArtistPlacement>> GetPlacementsAsync(string userId, string catalogVersion, CancellationToken ct)
    {
        await _indexes.Value;
        return await _placements.Find(p => p.UserId == userId && p.CatalogVersion == catalogVersion).ToListAsync(ct);
    }

    public async Task ReplaceCatalogAsync(UserCatalog catalog, IReadOnlyList<ArtistPlacement> placements, CancellationToken ct)
    {
        await _indexes.Value;
        if (placements.Count > 0)
            await _placements.InsertManyAsync(placements, new InsertManyOptions { IsOrdered = false }, ct);
        await _catalogs.ReplaceOneAsync(c => c.UserId == catalog.UserId, catalog, new ReplaceOptions { IsUpsert = true }, ct);
        await _placements.DeleteManyAsync(p => p.UserId == catalog.UserId && p.CatalogVersion != catalog.Version, ct);
    }

    public async Task<(long Plays, long Artists)> DeleteUserDataAsync(string userId, CancellationToken ct)
    {
        var plays = await _events.DeleteManyAsync(new BsonDocument(nameof(ListeningEvent.UserId), userId), ct);
        await _legacyCities.DeleteManyAsync(new BsonDocument(nameof(CityModel.UserId), userId), ct);
        await _catalogs.DeleteOneAsync(c => c.UserId == userId, ct);
        var artists = await _placements.DeleteManyAsync(p => p.UserId == userId, ct);
        return (plays.DeletedCount, artists.DeletedCount);
    }
}
