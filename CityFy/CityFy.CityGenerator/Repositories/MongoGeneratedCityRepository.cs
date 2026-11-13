using CityFy.CityGenerator.Models;
using MongoDB.Driver;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Repositories;

public sealed class MongoGeneratedCityRepository : IGeneratedCityRepository
{
    private const string Collection = "generated_cities";
    private const int TopGenresKept = 3;

    private readonly IMongoCollection<GeneratedCity> _cities;
    // Created once, on first use
    private readonly Lazy<Task> _indexes;

    public MongoGeneratedCityRepository(IMongoDatabase db)
    {
        _cities = db.GetCollection<GeneratedCity>(Collection);
        var keys = Builders<GeneratedCity>.IndexKeys;
        _indexes = new Lazy<Task>(() => _cities.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<GeneratedCity>(keys.Ascending(c => c.UserId).Descending(c => c.CreatedAt)),
            new CreateIndexModel<GeneratedCity>(keys.Ascending(c => c.UserId).Ascending(c => c.ParamsHash)),
            new CreateIndexModel<GeneratedCity>(keys.Ascending(c => c.Status)),
        ]));
    }

    private static ProjectionDefinition<GeneratedCity> WithoutContent => Builders<GeneratedCity>.Projection.Exclude(c => c.City).Exclude(c => c.Map);

    public async Task InsertAsync(GeneratedCity city, CancellationToken ct)
    {
        await _indexes.Value;
        await _cities.InsertOneAsync(city, cancellationToken: ct);
    }

    public async Task<GeneratedCity?> GetAsync(string cityId, CancellationToken ct) =>
        await _cities.Find(c => c.CityId == cityId).FirstOrDefaultAsync(ct);

    public async Task<GeneratedCity?> FindAsync(string userId, string paramsHash, string catalogVersion, IReadOnlyCollection<ProcessingStatus> statuses, CancellationToken ct) =>
        await _cities.Find(c => c.UserId == userId && c.ParamsHash == paramsHash && c.CatalogVersion == catalogVersion && statuses.Contains(c.Status))
            .Project<GeneratedCity>(WithoutContent)
            .SortByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<long> CountByUserAsync(string userId, CancellationToken ct) =>
        _cities.CountDocumentsAsync(c => c.UserId == userId, cancellationToken: ct);

    public async Task<IReadOnlyList<GeneratedCity>> ListByUserAsync(string userId, int skip, int take, CancellationToken ct) =>
        await _cities.Find(c => c.UserId == userId)
            .Project<GeneratedCity>(WithoutContent)
            .SortByDescending(c => c.CreatedAt)
            .Skip(skip).Limit(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GeneratedCity>> ListByStatusAsync(IReadOnlyCollection<ProcessingStatus> statuses, CancellationToken ct) =>
        await _cities.Find(c => statuses.Contains(c.Status)).Project<GeneratedCity>(WithoutContent).ToListAsync(ct);

    public async Task<bool> TryUpdateStatusAsync(string cityId, IReadOnlyCollection<ProcessingStatus> from, ProcessingStatus to,
        string? errorCode, string? message, DateTime now, CancellationToken ct)
    {
        var update = Builders<GeneratedCity>.Update
            .Set(c => c.Status, to)
            .Set(c => c.ErrorCode, errorCode)
            .Set(c => c.Message, message)
            .Set(c => c.UpdatedAt, now);
        if (to is ProcessingStatus.Failed or ProcessingStatus.Cancelled)
            update = update.Set(c => c.CompletedAt, now);

        var result = await _cities.UpdateOneAsync(c => c.CityId == cityId && from.Contains(c.Status), update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> CompleteAsync(string cityId, CityModel city, CityLayout map, string catalogVersion, DateTime now, CancellationToken ct)
    {
        var update = Builders<GeneratedCity>.Update
            .Set(c => c.Status, ProcessingStatus.Done)
            .Set(c => c.City, city)
            .Set(c => c.Map, map)
            .Set(c => c.CatalogVersion, catalogVersion)
            .Set(c => c.Districts, city.Districts.Count)
            .Set(c => c.Artists, city.Summary.PlacedArtists)
            .Set(c => c.TopGenres, city.Districts.Take(TopGenresKept).Select(d => d.Name).ToList())
            .Set(c => c.ErrorCode, null)
            .Set(c => c.Message, null)
            .Set(c => c.UpdatedAt, now)
            .Set(c => c.CompletedAt, now);

        var result = await _cities.UpdateOneAsync(c => c.CityId == cityId && c.Status == ProcessingStatus.InProgress, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string userId, string cityId, CancellationToken ct) =>
        (await _cities.DeleteOneAsync(c => c.CityId == cityId && c.UserId == userId, ct)).DeletedCount > 0;

    public async Task<long> DeleteByUserAsync(string userId, CancellationToken ct) =>
        (await _cities.DeleteManyAsync(c => c.UserId == userId, ct)).DeletedCount;
}
