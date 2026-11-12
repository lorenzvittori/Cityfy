using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityFy.Api.Models;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.Api.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static async Task<string?> CodeOf(HttpResponseMessage resp) =>
        JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString();

    private static CityModel SampleCity() => new()
    {
        UserId = "u1", GeneratedAt = DateTime.UtcNow,
        Summary = new CitySummary { TotalPlays = 10, QualifiedPlays = 10, PlacedArtists = 1, TotalArtists = 1, CoveragePercent = 100 },
        Districts =
        [
            new CityDistrict
            {
                DistrictId = "rock", Name = "rock", GenreId = 1, Color = "#ff0000", Score = 10, Density = 1, ListeningIntensity = 100,
                Blocks =
                [
                    new CityBlock
                    {
                        BlockId = "rock__grunge", Name = "grunge", GenreId = 2, Score = 10,
                        Buildings = [new CityBuilding { BuildingId = "mb-nirvana", Name = "Nirvana", QualifiedPlays = 10, Plays = 10, Height = 40, Width = 12, Color = "#ff0000" }]
                    }
                ]
            }
        ],
        Unresolved = [new UnresolvedArtist { Name = "Unknown", Plays = 3 }]
    };

    [Fact]
    public async Task WithoutSession_ApiAnswers401Unauthenticated()
    {
        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/cities", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(ErrorCodes.Unauthenticated, await CodeOf(resp));
    }

    [Fact]
    public async Task Login_SetsSession_AndRedirectsToTheFrontend()
    {
        _factory.Fakes.OnJson(HttpMethod.Post, "/internal/spotify/link", new { userId = "u1", displayName = "U" });

        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/auth/callback?code=c&state=s", Ct);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("http://frontend.test/", resp.Headers.Location!.ToString());
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), c => c.StartsWith("cityfy.auth=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoginFailure_RedirectsToTheLoginPageWithTheCode()
    {
        _factory.Fakes.On(HttpMethod.Post, "/internal/spotify/link", _ => FakeServices.Problem(HttpStatusCode.BadRequest, ErrorCodes.SpotifyLoginExpired));

        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/auth/callback?code=c&state=old", Ct);

        Assert.Equal("http://frontend.test/login?error=SPOTIFY_LOGIN_EXPIRED", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task UserDenial_RedirectsWithAuthDenied()
    {
        var resp = await _factory.CreateApiClient().GetAsync("/api/v1/auth/callback?error=access_denied", Ct);

        Assert.Equal("http://frontend.test/login?error=SPOTIFY_AUTH_DENIED", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task City_IsReadForTheSessionUser_AndMappedToV1()
    {
        var client = await _factory.LoginAsync("u1");
        var info = new GeneratedCityInfo("c1", null, null, null, "organic", null, [], [], "Done", null, DateTime.UtcNow, DateTime.UtcNow, "v1", 1, 1, ["rock"]);
        _factory.Fakes.OnJson(HttpMethod.Get, "/api/city-generator/u1/cities/c1", new GeneratedCityDetail(info, SampleCity(), null));

        var detail = await client.GetFromJsonAsync<GeneratedCityDetailDto>("/api/v1/cities/c1", Ct);

        var building = Assert.Single(Assert.Single(Assert.Single(detail!.City!.Districts).Blocks).Buildings);
        Assert.Equal("mb-nirvana", building.ArtistId);
        Assert.Equal(40, building.Height);
        // internal-only data is not exposed
        Assert.DoesNotContain("Unknown", await client.GetStringAsync("/api/v1/cities/c1", Ct));
    }

    [Fact]
    public async Task DownstreamErrorCode_IsForwardedUnchanged()
    {
        var client = await _factory.LoginAsync("u1");
        _factory.Fakes.On(HttpMethod.Get, "/api/city-generator/u1/cities/c1", _ => FakeServices.Problem(HttpStatusCode.NotFound, ErrorCodes.CityNotFound));

        var resp = await client.GetAsync("/api/v1/cities/c1", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(ErrorCodes.CityNotFound, await CodeOf(resp));
    }
    [Fact]
    public async Task UnreachableService_IsServiceUnavailable()
    {
        var client = await _factory.LoginAsync("u1");
        _factory.Fakes.On(HttpMethod.Get, "/api/pipeline/runs?userId=u1&limit=10", _ => throw new HttpRequestException("connection refused"));

        var resp = await client.GetAsync("/api/v1/pipeline/runs", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Equal(ErrorCodes.ServiceUnavailable, await CodeOf(resp));
    }

    [Fact]
    public async Task SomeoneElsesUpload_LooksNotFound()
    {
        var client = await _factory.LoginAsync("u1");
        _factory.Fakes.OnJson(HttpMethod.Get, "/internal/uploads/up1", new
        {
            uploadId = "up1", userId = "someone-else", status = "Done", inserted = 5, startedAt = DateTime.UtcNow
        });

        var resp = await client.GetAsync("/api/v1/uploads/up1", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(ErrorCodes.UploadNotFound, await CodeOf(resp));
    }

    [Fact]
    public async Task OwnUpload_StatusIncludesTheErrorCode()
    {
        var client = await _factory.LoginAsync("u1");
        _factory.Fakes.OnJson(HttpMethod.Get, "/internal/uploads/up1", new
        {
            uploadId = "up1", userId = "u1", status = "Failed", inserted = 0, startedAt = DateTime.UtcNow, errorCode = ErrorCodes.UploadNoStreamingFiles
        });

        var status = await client.GetFromJsonAsync<ImportStatusDto>("/api/v1/uploads/up1", Ct);

        Assert.Equal("Failed", status!.Status);
        Assert.Equal(ErrorCodes.UploadNoStreamingFiles, status.ErrorCode);
    }

    [Fact]
    public async Task Upload_IsForwardedWithTheSessionUser()
    {
        var client = await _factory.LoginAsync("u1");
        string? forwarded = null;
        _factory.Fakes.On(HttpMethod.Post, "/internal/uploads", req =>
        {
            forwarded = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return FakeServices.Json(new { id = "up9" });
        });

        using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "export.zip" } };
        var resp = await client.PostAsync("/api/v1/uploads", form, Ct);

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        Assert.Contains("name=UserId", forwarded);
        Assert.Contains("u1", forwarded);
    }
}
