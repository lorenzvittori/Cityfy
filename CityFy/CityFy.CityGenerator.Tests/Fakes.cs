using CityFy.CityGenerator.Clients;
using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.Repositories;
using ServiceDefault.City;
using ServiceDefault.Errors;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Tests;

/// <summary>'generated_cities' in memory (same filters as the Mongo repository).</summary>
public sealed class InMemoryGeneratedCityRepository : IGeneratedCityRepository
{
    public Dictionary<string, GeneratedCity> Cities { get; } = new();

    public Task InsertAsync(GeneratedCity city, CancellationToken ct)
    {
        Cities.Add(city.CityId, city);
        return Task.CompletedTask;
    }

    public Task<GeneratedCity?> GetAsync(string cityId, CancellationToken ct) => Task.FromResult(Cities.GetValueOrDefault(cityId));

    public Task<GeneratedCity?> FindAsync(string userId, string paramsHash, string catalogVersion, IReadOnlyCollection<ProcessingStatus> statuses, CancellationToken ct) =>
        Task.FromResult(Cities.Values
            .Where(c => c.UserId == userId && c.ParamsHash == paramsHash && c.CatalogVersion == catalogVersion && statuses.Contains(c.Status))
            .OrderByDescending(c => c.CreatedAt).FirstOrDefault());

    public Task<long> CountByUserAsync(string userId, CancellationToken ct) => Task.FromResult((long)Cities.Values.Count(c => c.UserId == userId));

    public Task<IReadOnlyList<GeneratedCity>> ListByUserAsync(string userId, int skip, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GeneratedCity>>(Cities.Values.Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt).Skip(skip).Take(take).ToList());

    public Task<IReadOnlyList<GeneratedCity>> ListByStatusAsync(IReadOnlyCollection<ProcessingStatus> statuses, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GeneratedCity>>(Cities.Values.Where(c => statuses.Contains(c.Status)).ToList());

    public Task<bool> TryUpdateStatusAsync(string cityId, IReadOnlyCollection<ProcessingStatus> from, ProcessingStatus to,
        string? errorCode, string? message, DateTime now, CancellationToken ct)
    {
        if (!Cities.TryGetValue(cityId, out var c) || !from.Contains(c.Status))
            return Task.FromResult(false);
        c.Status = to;
        c.ErrorCode = errorCode;
        c.Message = message;
        c.UpdatedAt = now;
        return Task.FromResult(true);
    }

    public Task<bool> CompleteAsync(string cityId, CityModel city, CityLayout map, string catalogVersion, DateTime now, CancellationToken ct)
    {
        if (!Cities.TryGetValue(cityId, out var c) || c.Status != ProcessingStatus.InProgress)
            return Task.FromResult(false);
        c.Status = ProcessingStatus.Done;
        c.City = city;
        c.Map = map;
        c.CatalogVersion = catalogVersion;
        c.Districts = city.Districts.Count;
        c.CompletedAt = now;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string userId, string cityId, CancellationToken ct) =>
        Task.FromResult(Cities.TryGetValue(cityId, out var c) && c.UserId == userId && Cities.Remove(cityId));

    public Task<long> DeleteByUserAsync(string userId, CancellationToken ct)
    {
        var ids = Cities.Values.Where(c => c.UserId == userId).Select(c => c.CityId).ToList();
        ids.ForEach(id => Cities.Remove(id));
        return Task.FromResult((long)ids.Count);
    }
}

/// <summary>Catalog with one catalog version per user; users without one get CITY_CATALOG_MISSING.</summary>
public sealed class FakeCatalogClient : ICatalogClient
{
    public Dictionary<string, string> Versions { get; } = new();
    public List<CityModelQuery> Queries { get; } = new();

    /// <summary>Called while the city model is computed (e.g. to cancel the job meanwhile).</summary>
    public Action? OnFetch { get; set; }

    public Task<UserCatalogVersion> GetVersionAsync(string userId, CancellationToken ct) =>
        Versions.TryGetValue(userId, out var v)
            ? Task.FromResult(new UserCatalogVersion(v, DateTime.UtcNow))
            : throw new CityFyException(ErrorCodes.CityCatalogMissing, "no catalog", 404);

    public Task<CityModelResult> GetCityModelAsync(string userId, CityModelQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        OnFetch?.Invoke();
        ct.ThrowIfCancellationRequested();
        var city = new CityModel
        {
            UserId = userId,
            Districts = [new CityDistrict { DistrictId = "rock", Name = "rock", Color = "#ff0000" }]
        };
        return Task.FromResult(new CityModelResult(city, Versions.GetValueOrDefault(userId, "v?")));
    }
}

public sealed class FakePublisher : IMessagePublisher
{
    public List<object> Published { get; } = new();

    public Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default) where T : class
    {
        Published.Add(message);
        return Task.CompletedTask;
    }
}
