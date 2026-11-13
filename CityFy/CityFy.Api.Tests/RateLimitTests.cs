using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CityFy.Api.Models;
using ServiceDefault.Errors;

namespace CityFy.Api.Tests;

/// <summary>Limits per client network on the public API (RateLimits section).</summary>
public sealed class RateLimitTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    /// <summary>A client of the network <paramref name="ip"/>, as the reverse proxy reports it.</summary>
    private HttpClient From(string ip)
    {
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    [Fact]
    public async Task SessionStarts_OverTheLimit_Are429WithRetryAfter_PerNetwork()
    {
        _factory.RateLimits["SessionStartsPerHour"] = "2";

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await From("203.0.113.7").PostAsync("/api/v1/session", null, Ct)).StatusCode);
        var rejected = await From("203.0.113.7").PostAsync("/api/v1/session", null, Ct);
        var otherNetwork = await From("198.51.100.1").PostAsync("/api/v1/session", null, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(ErrorCodes.RateLimited, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryAfterSeconds").GetInt32() > 0);
        Assert.Equal(HttpStatusCode.OK, otherNetwork.StatusCode);
        Assert.Equal(3, _factory.Sessions.Sessions.Count);
    }

    [Fact]
    public async Task LastFmLogin_OverTheLimit_GoesBackToTheWelcomePage()
    {
        _factory.RateLimits["LastFmLoginsPerHour"] = "1";
        _factory.Fakes.OnJson(HttpMethod.Get, "/internal/lastfm/authorize-url", new { url = "https://www.last.fm/api/auth/" });
        var client = From("203.0.113.7");

        await client.GetAsync("/api/v1/auth/lastfm/login", Ct);
        var rejected = await client.GetAsync("/api/v1/auth/lastfm/login", Ct);

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("http://frontend.test/welcome?error=RATE_LIMITED", rejected.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Uploads_OverTheDailyLimitOfTheUser_Are429()
    {
        _factory.RateLimits["UploadsPerUserPerDay"] = "1";
        _factory.Fakes.OnJson(HttpMethod.Post, "/internal/uploads", new { id = "up1" });
        var (client, _) = await _factory.StartAnonymousAsync();

        async Task<HttpStatusCode> UploadAsync()
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "export.zip" } };
            return (await client.PostAsync("/api/v1/uploads", form, Ct)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Accepted, await UploadAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, await UploadAsync());
    }

    [Fact]
    public async Task AnyRequest_OverTheLimitPerMinute_Is429_ButInternalEndpointsAreNotCounted()
    {
        _factory.RateLimits["ApiRequestsPerMinute"] = "3";
        var client = From("203.0.113.9");

        for (var i = 0; i < 3; i++)
            await client.GetAsync("/api/v1/me", Ct);
        var rejected = await client.GetAsync("/api/v1/me", Ct);
        var health = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, health.StatusCode);
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void ClientNetwork_IsTheIPv4Address_OrTheIPv6Slash64(string address, string expected) =>
        Assert.Equal(expected, ClientNetwork.From(IPAddress.Parse(address)).Key);

    [Fact]
    public void ClientNetwork_WithoutAddress_IsUnknown() =>
        Assert.Equal(ClientNetwork.Unknown, ClientNetwork.From(null));
}
