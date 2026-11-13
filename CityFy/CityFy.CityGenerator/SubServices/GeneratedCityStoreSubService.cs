using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.Options;
using CityFy.CityGenerator.Repositories;
using Microsoft.Extensions.Options;
using ServiceDefault.City;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.SubServices;

public sealed class GeneratedCityStoreSubService(
    IGeneratedCityRepository repo,
    CancellationRegistry running,
    IOptions<CityGeneratorOptions> options) : IGeneratedCityStoreSubService
{
    public const int MaxPageSize = 50;

    private static readonly ProcessingStatus[] Active = [ProcessingStatus.Pending, ProcessingStatus.InProgress];
    private static readonly ProcessingStatus[] Reusable = [ProcessingStatus.Pending, ProcessingStatus.InProgress, ProcessingStatus.Done];

    public Task<GeneratedCity?> FindReusableAsync(string userId, string paramsHash, string catalogVersion, CancellationToken ct) =>
        repo.FindAsync(userId, paramsHash, catalogVersion, Reusable, ct);

    public async Task EnsureBelowLimitAsync(string userId, CancellationToken ct)
    {
        var max = options.Value.MaxCitiesPerUser;
        if (await repo.CountByUserAsync(userId, ct) >= max)
            throw new CityFyException(ErrorCodes.CityLimitReached, $"User '{userId}' already has {max} cities", StatusCodes.Status409Conflict);
    }

    public async Task<bool> HasAnyAsync(string userId, CancellationToken ct) => await repo.CountByUserAsync(userId, ct) > 0;

    public async Task<GeneratedCity> CreateAsync(string userId, CityRequest request, string paramsHash, string catalogVersion, DateTime now, CancellationToken ct)
    {
        var city = new GeneratedCity
        {
            CityId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Name = request.Name,
            From = request.From,
            To = request.To,
            Layout = request.Layout,
            Recipe = request.Recipe,
            Genres = request.Genres.ToList(),
            Subgenres = request.Subgenres.ToList(),
            MaxDistricts = request.Districts.Max,
            ExtraGenres = request.Districts.ExtraName,
            ParamsHash = paramsHash,
            CatalogVersion = catalogVersion,
            Status = ProcessingStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        await repo.InsertAsync(city, ct);
        return city;
    }

    public Task<GeneratedCity?> GetAsync(string cityId, CancellationToken ct) => repo.GetAsync(cityId, ct);

    public async Task<GeneratedCity> GetOwnedAsync(string userId, string cityId, CancellationToken ct)
    {
        var city = await repo.GetAsync(cityId, ct);
        // Another user's city answers exactly like a missing one
        return city != null && city.UserId == userId
            ? city
            : throw new CityFyException(ErrorCodes.CityNotFound, $"City '{cityId}' not found", StatusCodes.Status404NotFound);
    }

    public async Task<(IReadOnlyList<GeneratedCity> Items, int Page, int PageSize, long Total)> ListAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        var p = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var total = await repo.CountByUserAsync(userId, ct);
        var items = await repo.ListByUserAsync(userId, (p - 1) * size, size, ct);
        return (items, p, size, total);
    }

    public Task<IReadOnlyList<GeneratedCity>> ListUnfinishedAsync(CancellationToken ct) => repo.ListByStatusAsync(Active, ct);

    public bool IsFinished(GeneratedCity city) => !Active.Contains(city.Status);

    public async Task<CancellationTokenSource?> TryStartAsync(string cityId, CancellationToken ct)
    {
        var cts = running.Register(cityId, ct);

        // Cancelled or deleted between the first read and the registration? (an interrupted
        // InProgress job, re-queued at startup, starts again)
        if (!await repo.TryUpdateStatusAsync(cityId, Active, ProcessingStatus.InProgress, null, null, DateTime.UtcNow, CancellationToken.None))
        {
            running.Unregister(cityId, cts);
            return null;
        }
        return cts;
    }

    // Status updates use CancellationToken.None: they must be saved even when the job is stopping
    public Task<bool> CompleteAsync(string cityId, CityModelResult result, CityLayout map, CancellationToken ct) =>
        repo.CompleteAsync(cityId, result.City, map, result.CatalogVersion, DateTime.UtcNow, CancellationToken.None);

    public Task FailAsync(string cityId, string errorCode, string message, CancellationToken ct) =>
        repo.TryUpdateStatusAsync(cityId, Active, ProcessingStatus.Failed, errorCode, message, DateTime.UtcNow, CancellationToken.None);

    public void End(string cityId, CancellationTokenSource cts) => running.Unregister(cityId, cts);

    public async Task CancelAsync(GeneratedCity city, CancellationToken ct)
    {
        // Status first (a queued job will be skipped), then stop the running one if any
        if (!await repo.TryUpdateStatusAsync(city.CityId, Active, ProcessingStatus.Cancelled, ErrorCodes.ProcessCancelled,
                "Cancelled by the user", DateTime.UtcNow, CancellationToken.None))
            throw new CityFyException(ErrorCodes.ProcessNotCancellable, $"City '{city.CityId}' is already finished", StatusCodes.Status409Conflict);
        running.Cancel(city.CityId);
    }

    public async Task CancelIfActiveAsync(GeneratedCity city, CancellationToken ct)
    {
        if (await repo.TryUpdateStatusAsync(city.CityId, Active, ProcessingStatus.Cancelled, ErrorCodes.ProcessCancelled,
                "Cancelled: the city was deleted", DateTime.UtcNow, CancellationToken.None))
            running.Cancel(city.CityId);
    }

    public async Task DeleteAsync(string userId, string cityId, CancellationToken ct)
    {
        if (!await repo.DeleteAsync(userId, cityId, ct))
            throw new CityFyException(ErrorCodes.CityNotFound, $"City '{cityId}' not found", StatusCodes.Status404NotFound);
    }

    public async Task<long> DeleteAllAsync(string userId, CancellationToken ct)
    {
        foreach (var city in await repo.ListByStatusAsync(Active, ct))
            if (city.UserId == userId)
                await CancelIfActiveAsync(city, ct);
        return await repo.DeleteByUserAsync(userId, ct);
    }
}
