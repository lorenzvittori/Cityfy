using ServiceDefault.City;
using CityFy.Elaboration.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ServiceDefault.Listening;

namespace CityFy.Elaboration.Repositories;

public interface IElaborationRepository
{
    /// <summary>Per-artist play totals of a user since a date (all history when null).</summary>
    Task<List<ArtistPlays>> AggregateArtistPlaysAsync(string userId, DateTime? since, int qualifiedPlayMs, CancellationToken ct);

    Task SaveCityAsync(CityModel city, CancellationToken ct);
    Task<CityModel?> GetCityAsync(string userId, string period, CancellationToken ct);

    /// <summary>Has the user any play at all (any period, any source)?</summary>
    Task<bool> HasPlaysAsync(string userId, CancellationToken ct);

    /// <summary>Data deletion: every play and every city of the user.</summary>
    Task<(long Plays, long Cities)> DeleteUserDataAsync(string userId, CancellationToken ct);
}

public class ElaborationRepository : IElaborationRepository
{
    private const string CityCollection = "city_models";

    private readonly IMongoCollection<BsonDocument> _events;
    private readonly IMongoCollection<CityModel> _cities;

    public ElaborationRepository(IMongoDatabase db)
    {
        _events = db.GetCollection<BsonDocument>(ListeningEvent.CollectionName);
        _cities = db.GetCollection<CityModel>(CityCollection);
    }

    public async Task<List<ArtistPlays>> AggregateArtistPlaysAsync(string userId, DateTime? since, int qualifiedPlayMs, CancellationToken ct)
    {
        var match = new BsonDocument
        {
            { nameof(ListeningEvent.UserId), userId },
            { nameof(ListeningEvent.ArtistName), new BsonDocument("$ne", BsonNull.Value) }
        };
        if (since.HasValue)
            match.Add(nameof(ListeningEvent.PlayedAt), new BsonDocument("$gte", since.Value));

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

    public Task SaveCityAsync(CityModel city, CancellationToken ct) =>
        _cities.ReplaceOneAsync(c => c.Id == city.Id, city, new ReplaceOptions { IsUpsert = true }, ct);

    public async Task<CityModel?> GetCityAsync(string userId, string period, CancellationToken ct)
    {
        var id = CityModel.BuildId(userId, period);
        return await _cities.Find(c => c.Id == id).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> HasPlaysAsync(string userId, CancellationToken ct) =>
        await _events.Find(new BsonDocument(nameof(ListeningEvent.UserId), userId)).Limit(1).AnyAsync(ct);

    public async Task<(long Plays, long Cities)> DeleteUserDataAsync(string userId, CancellationToken ct)
    {
        var plays = await _events.DeleteManyAsync(new BsonDocument(nameof(ListeningEvent.UserId), userId), ct);
        var cities = await _cities.DeleteManyAsync(c => c.UserId == userId, ct);
        return (plays.DeletedCount, cities.DeletedCount);
    }
}
