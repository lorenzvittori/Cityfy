using CityFy.CityGenerator.Mappers;
using CityFy.CityGenerator.Options;
using CityFy.CityGenerator.Services;
using CityFy.CityGenerator.SubServices;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceDefault.City;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Tests;

public sealed class CityGenerationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryGeneratedCityRepository _repo = new();
    private readonly FakeCatalogClient _catalog = new();
    private readonly FakePublisher _publisher = new();
    private readonly CancellationRegistry _running = new();
    private readonly CityGenerationService _service;

    public CityGenerationTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CityGeneratorOptions { MaxCitiesPerUser = 3 });
        _service = new CityGenerationService(
            new CityRequestSubService(options),
            new GeneratedCityStoreSubService(_repo, _running, options),
            new CityModelFetchSubService(_catalog),
            new CityLayoutSubService(),
            new LayoutRecipeParseSubService(),
            new GenerationEventsSubService(_publisher),
            new GenerationErrorSubService(),
            new GeneratedCityMapper(),
            NullLogger<CityGenerationService>.Instance);
        _catalog.Versions["u1"] = "v1";
        _catalog.Versions["u2"] = "v1";
    }

    private static GenerateCityRequest Rock(string? name = null) =>
        new(name, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "grid", Genres: [1]);

    [Fact]
    public async Task Request_QueuesAPendingCity_AndPublishesTheJob()
    {
        var city = await _service.RequestAsync("u1", Rock("My city"), Ct);

        Assert.Equal(nameof(ProcessingStatus.Pending), city.Status);
        Assert.Equal(("My city", "grid", "v1"), (city.Name, city.Layout, city.CatalogVersion));
        var message = Assert.IsType<GenerateCityRequested>(Assert.Single(_publisher.Published));
        Assert.Equal(city.CityId, message.CityId);
    }

    [Fact]
    public async Task IdenticalRequest_OnTheSameCatalog_ReusesTheCity_WhateverTheNameAndGenreOrder()
    {
        var first = await _service.RequestAsync("u1", Rock() with { Genres = [2, 1] }, Ct);
        var second = await _service.RequestAsync("u1", Rock("other name") with { Genres = [1, 2, 2] }, Ct);

        Assert.Equal(first.CityId, second.CityId);
        Assert.Single(_publisher.Published);

        // A new catalog version computes a new city
        _catalog.Versions["u1"] = "v2";
        var third = await _service.RequestAsync("u1", Rock() with { Genres = [1, 2] }, Ct);
        Assert.NotEqual(first.CityId, third.CityId);
    }

    [Fact]
    public async Task CustomMap_StoresTheCanonicalString_AndIsReusedWhateverTheSpelling()
    {
        var first = await _service.RequestAsync("u1", Rock() with { Layout = "custom", Recipe = "BASE=grid; blocks=0.60" }, Ct);
        var second = await _service.RequestAsync("u1", Rock() with { Layout = "custom", Recipe = "blocks=0.6;base=grid" }, Ct);

        Assert.Equal(first.CityId, second.CityId);
        Assert.Equal("shape=rectangle; regularity=1; blocks=0.6; streets=3; avenues=6; plazas=off; heights=log; lamps=1", first.Recipe);

        await _service.GenerateAsync(first.CityId, Ct);
        Assert.Equal("custom", _repo.Cities[first.CityId].Map!.Template);
    }

    [Fact]
    public async Task InvalidCustomMap_IsRejectedWithProblems_AndNothingIsStored()
    {
        var error = await Assert.ThrowsAsync<CityFyException>(() =>
            _service.RequestAsync("u1", Rock() with { Layout = "custom", Recipe = "blocks=9; colour=red" }, Ct));

        Assert.Equal(ErrorCodes.CityInvalidRecipe, error.Code);
        Assert.Equal(2, ((IReadOnlyList<RecipeProblem>)error.Extensions["problems"]!).Count);
        Assert.Empty(_repo.Cities);
    }

    [Fact]
    public async Task BeyondTheLimit_TheRequestIsRejected()
    {
        for (var year = 2020; year < 2023; year++)
            await _service.RequestAsync("u1", Rock() with { From = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, Ct);

        var error = await Assert.ThrowsAsync<CityFyException>(() => _service.RequestAsync("u1", Rock(), Ct));

        Assert.Equal((ErrorCodes.CityLimitReached, 409), (error.Code, error.StatusCode));
    }

    [Fact]
    public async Task BeforeTheFirstImport_TheRequestFailsWithCatalogMissing()
    {
        var error = await Assert.ThrowsAsync<CityFyException>(() => _service.RequestAsync("nobody", Rock(), Ct));

        Assert.Equal(ErrorCodes.CityCatalogMissing, error.Code);
        Assert.Empty(_repo.Cities);
    }

    [Fact]
    public async Task Generate_FetchesTheCityModel_AndCompletesTheCity()
    {
        var city = await _service.RequestAsync("u1", Rock(), Ct);

        await _service.GenerateAsync(city.CityId, Ct);

        var detail = await _service.GetAsync("u1", city.CityId, Ct);
        Assert.Equal(nameof(ProcessingStatus.Done), detail.Info.Status);
        Assert.Equal("rock", Assert.Single(detail.City!.Districts).Name);
        var query = Assert.Single(_catalog.Queries);
        Assert.Equal((Rock().From, Rock().To), (query.From, query.To));
        Assert.Equal([1], query.Genres!);
    }

    [Fact]
    public async Task CancelledWhileRunning_TheResultIsDropped_AndStaysCancelled()
    {
        var city = await _service.RequestAsync("u1", Rock(), Ct);
        _catalog.OnFetch = () => _service.CancelAsync("u1", city.CityId, Ct).GetAwaiter().GetResult();

        await _service.GenerateAsync(city.CityId, Ct);

        var stored = _repo.Cities[city.CityId];
        Assert.Equal(ProcessingStatus.Cancelled, stored.Status);
        Assert.Null(stored.City);
    }

    [Fact]
    public async Task CancelledWhileQueued_TheJobDoesNothing_AndCannotBeCancelledTwice()
    {
        var city = await _service.RequestAsync("u1", Rock(), Ct);
        await _service.CancelAsync("u1", city.CityId, Ct);

        await _service.GenerateAsync(city.CityId, Ct);

        Assert.Empty(_catalog.Queries);
        var error = await Assert.ThrowsAsync<CityFyException>(() => _service.CancelAsync("u1", city.CityId, Ct));
        Assert.Equal(ErrorCodes.ProcessNotCancellable, error.Code);
    }

    [Fact]
    public async Task FinalFailure_StoresTheErrorCode()
    {
        var city = await _service.RequestAsync("u1", Rock(), Ct);

        await _service.MarkFailedAsync(city.CityId, new CityFyException(ErrorCodes.ServiceUnavailable, "down", 503), Ct);

        var stored = _repo.Cities[city.CityId];
        Assert.Equal((ProcessingStatus.Failed, ErrorCodes.ServiceUnavailable), (stored.Status, stored.ErrorCode));
    }

    [Fact]
    public async Task AnotherUsersCity_IsNotFound_ForEveryOperation()
    {
        var city = await _service.RequestAsync("u1", Rock(), Ct);

        foreach (var call in new Func<Task>[]
                 {
                     () => _service.GetAsync("u2", city.CityId, Ct),
                     () => _service.CancelAsync("u2", city.CityId, Ct),
                     () => _service.DeleteAsync("u2", city.CityId, Ct),
                 })
            Assert.Equal(ErrorCodes.CityNotFound, (await Assert.ThrowsAsync<CityFyException>(call)).Code);

        Assert.True(_repo.Cities.ContainsKey(city.CityId));
    }

    [Fact]
    public async Task List_IsPaged_MostRecentFirst()
    {
        for (var year = 2020; year < 2023; year++)
            await _service.RequestAsync("u1", Rock() with { From = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, Ct);

        var page = await _service.ListAsync("u1", 2, 2, Ct);

        Assert.Equal((2, 2, 3L), (page.Page, page.PageSize, page.Total));
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task FirstCatalog_QueuesTheDefaultCity_OnlyForUsersWithoutCities()
    {
        var updated = new UserCatalogUpdated("r1", "u2", "v1", 10, 8, DateTime.UtcNow);

        await _service.HandleCatalogUpdatedAsync(updated, Ct);
        await _service.HandleCatalogUpdatedAsync(updated, Ct);

        var city = Assert.Single(_repo.Cities.Values);
        Assert.Equal(("organic", null, null), (city.Layout, city.From, city.To));
        Assert.Empty(city.Genres);
    }

    [Fact]
    public async Task Delete_StopsTheJob_AndRemovesTheCity()
    {
        var city = await _service.RequestAsync("u1", Rock(), Ct);

        await _service.DeleteAsync("u1", city.CityId, Ct);
        await _service.GenerateAsync(city.CityId, Ct); // queued message arrives later

        Assert.Empty(_repo.Cities);
        Assert.Empty(_catalog.Queries);
    }
}
