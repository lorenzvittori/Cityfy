using CityFy.CityGenerator.Mappers;
using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.SubServices;
using ServiceDefault.City;
using ServiceDefault.Contracts;

namespace CityFy.CityGenerator.Services;

public sealed class CityGenerationService(
    ICityRequestSubService requests,
    IGeneratedCityStoreSubService store,
    ICityModelFetchSubService cityModels,
    ICityLayoutSubService layouts,
    ILayoutRecipeParseSubService recipes,
    IGenerationEventsSubService events,
    IGenerationErrorSubService errors,
    IGeneratedCityMapper mapper,
    ILogger<CityGenerationService> logger) : ICityGenerationService
{
    public async Task<GeneratedCityInfo> RequestAsync(string userId, GenerateCityRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var city = requests.Validate(request, now);
        var recipe = recipes.Resolve(city.Layout, city.Recipe); // CITY_INVALID_RECIPE before anything is stored
        city = city with { Recipe = city.Layout == CityRequest.Custom ? recipes.Canonical(recipe) : null };
        var hash = requests.Hash(city);
        var version = await cityModels.GetCatalogVersionAsync(userId, ct);

        if (await store.FindReusableAsync(userId, hash, version, ct) is { } existing)
        {
            logger.LogInformation("City request of {UserId} reuses city {CityId} ({Status})", userId, existing.CityId, existing.Status);
            return mapper.ToInfo(existing);
        }

        await store.EnsureBelowLimitAsync(userId, ct);
        var created = await store.CreateAsync(userId, city, hash, version, now, ct);
        await events.PublishRequestedAsync(created, ct);

        logger.LogInformation("City {CityId} of {UserId} queued ({Layout}, {From} - {To}, {Genres} genres, {Subgenres} subgenres)",
            created.CityId, userId, created.Layout, created.From, created.To, created.Genres.Count, created.Subgenres.Count);
        return mapper.ToInfo(created);
    }

    public RecipeSchema GetRecipeSchema() => recipes.Schema();

    public async Task<GeneratedCityPage> ListAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        var (items, p, size, total) = await store.ListAsync(userId, page, pageSize, ct);
        return new GeneratedCityPage(items.Select(mapper.ToInfo).ToList(), p, size, total);
    }

    public async Task<GeneratedCityDetail> GetAsync(string userId, string cityId, CancellationToken ct) =>
        mapper.ToDetail(await store.GetOwnedAsync(userId, cityId, ct));

    public async Task DeleteAsync(string userId, string cityId, CancellationToken ct)
    {
        var city = await store.GetOwnedAsync(userId, cityId, ct);
        await store.CancelIfActiveAsync(city, ct);
        await store.DeleteAsync(userId, cityId, ct);
        logger.LogInformation("City {CityId} of {UserId} deleted", cityId, userId);
    }

    public async Task<GeneratedCityInfo> CancelAsync(string userId, string cityId, CancellationToken ct)
    {
        var city = await store.GetOwnedAsync(userId, cityId, ct);
        await store.CancelAsync(city, ct);
        logger.LogInformation("City {CityId} of {UserId} cancelled", cityId, userId);
        return mapper.ToInfo(await store.GetOwnedAsync(userId, cityId, ct));
    }

    public async Task GenerateAsync(string cityId, CancellationToken ct)
    {
        var city = await store.GetAsync(cityId, ct);
        if (city == null)
        {
            logger.LogInformation("City {CityId} no longer exists, generation skipped", cityId);
            return;
        }

        // Duplicate message (startup recovery + queued message) or cancelled while queued
        if (store.IsFinished(city))
        {
            logger.LogInformation("City {CityId} already {Status}, skipped", cityId, city.Status);
            return;
        }

        if (await store.TryStartAsync(cityId, ct) is not { } cts)
            return;

        try
        {
            var result = await cityModels.FetchAsync(city, cts.Token);
            var map = layouts.Compute(result.City, recipes.Resolve(city.Layout, city.Recipe));

            if (await store.CompleteAsync(cityId, result, map, cts.Token))
                logger.LogInformation("City {CityId}: {Districts} districts, {Artists} artists, {Roads} roads (catalog {Version})",
                    cityId, result.City.Districts.Count, map.Buildings.Count, map.Streets.Roads.Count, result.CatalogVersion);
            else
                logger.LogInformation("City {CityId} cancelled or deleted while generating: result dropped", cityId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Cancelled by the user (status already Cancelled)
            logger.LogInformation("City {CityId} generation stopped", cityId);
        }
        finally
        {
            store.End(cityId, cts);
        }
    }

    public Task MarkFailedAsync(string cityId, Exception error, CancellationToken ct) =>
        store.FailAsync(cityId, errors.ToErrorCode(error), error.Message, ct);

    public async Task HandleCatalogUpdatedAsync(UserCatalogUpdated message, CancellationToken ct)
    {
        if (await store.HasAnyAsync(message.UserId, ct))
            return;

        var created = await store.CreateAsync(message.UserId, CityRequest.Default, requests.Hash(CityRequest.Default), message.CatalogVersion, DateTime.UtcNow, ct);
        await events.PublishRequestedAsync(created, ct);
        logger.LogInformation("First city {CityId} of {UserId} queued", created.CityId, message.UserId);
    }

    public async Task<int> RequeueUnfinishedAsync(CancellationToken ct)
    {
        var unfinished = await store.ListUnfinishedAsync(ct);
        foreach (var city in unfinished)
            await events.PublishRequestedAsync(city, ct);

        if (unfinished.Count > 0)
            logger.LogInformation("Re-queued {Count} unfinished cities", unfinished.Count);
        return unfinished.Count;
    }
}
