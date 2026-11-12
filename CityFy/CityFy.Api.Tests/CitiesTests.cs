using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.Api.Tests;

public sealed class CitiesTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static GeneratedCityInfo Info(string status = "Pending") => new(
        "c1", null, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "grid", null, [1], [], status, null,
        DateTime.UtcNow, null, "v1", 0, 0, []);

    [Fact]
    public async Task Create_ForwardsTheRequestOfTheLoggedInUser_AndAnswers202()
    {
        GenerateCityRequest? forwarded = null;
        _factory.Fakes.On(HttpMethod.Post, "/api/city-generator/u1/cities", req =>
        {
            forwarded = req.Content!.ReadFromJsonAsync<GenerateCityRequest>(Ct).GetAwaiter().GetResult();
            return FakeServices.Json(Info(), HttpStatusCode.Accepted);
        });
        var client = await _factory.LoginAsync("u1");

        var resp = await client.PostAsJsonAsync("/api/v1/cities", new { layout = "grid", from = "2024-01-01T00:00:00Z", genres = new[] { 1 } }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        Assert.Equal("/api/v1/cities/c1", resp.Headers.Location!.AbsolutePath);
        Assert.Equal(("grid", 1), (forwarded!.Layout, Assert.Single(forwarded.Genres!)));
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LimitReached_KeepsTheCodeOfTheGenerator()
    {
        _factory.Fakes.On(HttpMethod.Post, "/api/city-generator/u1/cities", _ => FakeServices.Problem(HttpStatusCode.Conflict, ErrorCodes.CityLimitReached));
        var client = await _factory.LoginAsync("u1");

        var resp = await client.PostAsJsonAsync("/api/v1/cities", new { }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(ErrorCodes.CityLimitReached, (await resp.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidRecipe_ForwardsTheProblemsOfTheGenerator()
    {
        _factory.Fakes.On(HttpMethod.Post, "/api/city-generator/u1/cities", _ => FakeServices.Json(new
        {
            status = 400, title = ErrorCodes.CityInvalidRecipe, code = ErrorCodes.CityInvalidRecipe, detail = "bad",
            problems = new[] { new { key = "blocks", code = "OUT_OF_RANGE" } }
        }, HttpStatusCode.BadRequest, "application/problem+json"));
        var client = await _factory.LoginAsync("u1");

        var resp = await client.PostAsJsonAsync("/api/v1/cities", new { layout = "custom", recipe = "blocks=9" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(ErrorCodes.CityInvalidRecipe, body.GetProperty("code").GetString());
        Assert.Equal("blocks", body.GetProperty("problems")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task ListDetailAndDelete_UseTheLoggedInUser()
    {
        _factory.Fakes.OnJson(HttpMethod.Get, "/api/city-generator/u1/cities?page=2&pageSize=5", new GeneratedCityPage([Info("Done")], 2, 5, 6));
        _factory.Fakes.OnJson(HttpMethod.Get, "/api/city-generator/u1/cities/c1", new GeneratedCityDetail(Info("Done"),
            new CityModel { UserId = "u1", Districts = [new CityDistrict { DistrictId = "rock", Name = "rock", Color = "#f00" }] },
            new CityLayout("grid", [], [], [], [], new Streets([], [], [], [], [], []), new LayoutBounds(0, 1, 0, 2), 1, 2)));
        _factory.Fakes.On(HttpMethod.Delete, "/api/city-generator/u1/cities/c1", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = await _factory.LoginAsync("u1");

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/cities?page=2&pageSize=5", Ct);
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/v1/cities/c1", Ct);
        var deleted = await client.DeleteAsync("/api/v1/cities/c1", Ct);

        Assert.Equal(6, page.GetProperty("total").GetInt64());
        Assert.Equal("Done", page.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("rock", detail.GetProperty("city").GetProperty("districts")[0].GetProperty("name").GetString());
        Assert.Equal(2, detail.GetProperty("map").GetProperty("depth").GetDouble());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task GenreOptions_ComeFromTheCatalog()
    {
        _factory.Fakes.OnJson(HttpMethod.Get, "/api/catalog/genres/u1?from=2024-01-01T00%3A00%3A00.0000000Z",
            new[] { new GenreOption(1, "rock", "#f00", 10, 2, [new SubgenreOption(3, "grunge", 6, 1)]) });
        var client = await _factory.LoginAsync("u1");

        var genres = await client.GetFromJsonAsync<JsonElement>("/api/v1/city/genres?from=2024-01-01T00:00:00Z", Ct);

        Assert.Equal("grunge", genres[0].GetProperty("subgenres")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Anonymous_CannotUseTheCities()
    {
        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/cities", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
