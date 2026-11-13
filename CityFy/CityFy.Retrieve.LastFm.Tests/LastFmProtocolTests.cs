using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CityFy.Retrieve.LastFm.Clients;
using CityFy.Retrieve.LastFm.Mappers;
using CityFy.Retrieve.LastFm.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace CityFy.Retrieve.LastFm.Tests;

/// <summary>Signature, JSON of Last.fm, client retries and the request gate.</summary>
public sealed class LastFmProtocolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Md5(string s) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(s)));

    // --- signature -------------------------------------------------------------------------

    [Fact]
    public void Signature_IsTheExampleOfTheDocumentation()
    {
        // webauth: md5("api_keyxxxxxxxxmethodauth.getSessiontokenxxxxxxxmysecret")
        var request = LastFmRequest.Of("auth.getSession", ("token", "xxxxxxx"), ("api_key", "xxxxxxxx"));

        Assert.Equal(Md5("api_keyxxxxxxxxmethodauth.getSessiontokenxxxxxxxmysecret"), request.Signature("mysecret"));
    }

    [Fact]
    public void Signature_SortsByName_AndIgnoresFormatAndCallback()
    {
        var request = LastFmRequest.Of("user.getRecentTracks", ("user", "alice"), ("format", "json"), ("callback", "cb"), ("api_key", "k"), ("sk", "s"));

        Assert.Equal(Md5("api_keykmethoduser.getRecentTrackssksuseralicesecret"), request.Signature("secret"));
    }

    [Fact]
    public void QueryString_EscapesValues_AndSkipsMissingOnes()
    {
        var request = LastFmRequest.Of("user.getRecentTracks", ("user", "a b&c"), ("from", null));

        Assert.Equal("?method=user.getRecentTracks&user=a%20b%26c&api_sig=x", request.ToQueryString("x"));
    }

    // --- JSON ------------------------------------------------------------------------------

    private static readonly LastFmResponseMapper Mapper = new();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void RecentTracks_SeparatesNowPlaying_AndReadsExtendedArtists()
    {
        var page = Mapper.ToRecentTracks(Json("""
            {"recenttracks":{"track":[
              {"artist":{"name":"Radiohead","mbid":"a74b1b7f"},"name":"Reckoner","mbid":"","album":{"#text":"In Rainbows","mbid":""},"@attr":{"nowplaying":"true"}},
              {"artist":{"name":"Björk","mbid":""},"name":"Jóga","mbid":"t1","album":{"#text":""},"date":{"uts":"1700000000","#text":"14 Nov 2023, 22:13"}}
            ],"@attr":{"user":"alice","page":"1","perPage":"200","totalPages":"34","total":"6700"}}}
            """));

        Assert.Equal(new LastFmNowPlaying("Radiohead", "Reckoner", "In Rainbows", "a74b1b7f"), page.NowPlaying);
        var s = Assert.Single(page.Scrobbles);
        Assert.Equal(("Björk", "Jóga", null, null, "t1"), (s.Artist, s.Track, s.Album, s.ArtistMbid, s.TrackMbid));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, s.ScrobbledAt);
        Assert.Equal((1, 34, 6700), (page.Page, page.TotalPages, page.Total));
    }

    [Fact]
    public void RecentTracks_WithASingleTrack_IsAnObjectNotAnArray()
    {
        var page = Mapper.ToRecentTracks(Json("""
            {"recenttracks":{"track":{"artist":{"#text":"Muse"},"name":"Uprising","date":{"uts":"1700000100"}},"@attr":{"totalPages":"1","total":"1"}}}
            """));

        Assert.Equal("Muse", Assert.Single(page.Scrobbles).Artist);
    }

    [Fact]
    public void RecentTracks_SkipsScrobblesWithoutDateOrName_AndReadsAnEmptyHistory()
    {
        var page = Mapper.ToRecentTracks(Json("""
            {"recenttracks":{"track":[{"artist":{"name":"X"},"name":"No date"},{"artist":{"name":""},"name":"No artist","date":{"uts":"1"}}],
             "@attr":{"totalPages":"0","total":"0"}}}
            """));

        Assert.Empty(page.Scrobbles);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void ErrorBody_IsRecognised()
    {
        var error = Mapper.ToError(Json("""{"error":29,"message":"Rate limit exceeded"}"""));

        Assert.Equal(LastFmApiException.RateLimitExceeded, error!.Code);
        Assert.True(error.IsTransient);
        Assert.Null(Mapper.ToError(Json("""{"recenttracks":{}}""")));
    }

    [Fact]
    public void Session_IsRead_AndAMissingKeyIsInvalidData()
    {
        Assert.Equal(new LastFmSession("alice", "sk1"), Mapper.ToSession(Json("""{"session":{"name":"alice","key":"sk1","subscriber":0}}""")));
        Assert.Throws<InvalidDataException>(() => Mapper.ToSession(Json("""{"session":{"name":"alice"}}""")));
    }

    // --- client ----------------------------------------------------------------------------

    private static (LastFmClient Client, StubHttpHandler Http, LastFmRequestGate Gate) Client()
    {
        var options = TestOptions.Create();
        var http = new StubHttpHandler();
        var gate = new LastFmRequestGate(options);
        var client = new LastFmClient(new HttpClient(http), gate, Mapper, options, NullLogger<LastFmClient>.Instance);
        return (client, http, gate);
    }

    private const string EmptyPage = """{"recenttracks":{"track":[],"@attr":{"page":"1","totalPages":"0","total":"0"}}}""";

    [Fact]
    public async Task RateLimit_PausesTheGate_AndRetries()
    {
        var (client, http, gate) = Client();
        http.Respond(HttpStatusCode.TooManyRequests, """{"error":29,"message":"Rate limit exceeded"}""").Respond(HttpStatusCode.OK, EmptyPage);

        var started = DateTime.UtcNow;
        var page = await client.GetRecentTracksAsync("alice", null, null, null, 1, Ct);

        Assert.Empty(page.Scrobbles);
        Assert.Equal(2, http.Requests.Count);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(0.9)); // waited the 1 s pause
        Assert.Equal(TimeSpan.FromSeconds(1), gate.NextPause); // reset after the success
    }

    [Fact]
    public async Task InvalidSessionKey_IsNotRetried()
    {
        var (client, http, _) = Client();
        http.Respond(HttpStatusCode.Forbidden, """{"error":9,"message":"Invalid session key"}""");

        var ex = await Assert.ThrowsAsync<LastFmApiException>(() => client.GetRecentTracksAsync("alice", "sk", null, null, 1, Ct));

        Assert.Equal(LastFmApiException.InvalidSessionKey, ex.Code);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task ServerError_IsRetried_ThenFailsWithTheStatus()
    {
        var (client, http, _) = Client();
        http.Respond(HttpStatusCode.BadGateway, "<html>").Respond(HttpStatusCode.BadGateway, "<html>").Respond(HttpStatusCode.BadGateway, "<html>");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetRecentTracksAsync("alice", null, null, null, 1, Ct));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(3, http.Requests.Count); // MaxAttempts
    }

    [Fact]
    public async Task SignedRequest_CarriesKeySessionSignatureAndJsonFormat_AndExclusiveBounds()
    {
        var (client, http, _) = Client();
        http.Respond(HttpStatusCode.OK, EmptyPage);

        await client.GetRecentTracksAsync("alice", "sk", DateTimeOffset.FromUnixTimeSeconds(100).UtcDateTime, DateTimeOffset.FromUnixTimeSeconds(200).UtcDateTime, 3, Ct);

        var query = http.Requests[0].RequestUri!.Query;
        Assert.Contains("api_key=key", query);
        Assert.Contains("sk=sk", query);
        Assert.Contains("api_sig=", query);
        Assert.Contains("extended=1", query);
        Assert.Contains("page=3", query);
        Assert.Contains("from=101", query);
        Assert.Contains("to=199", query);
        Assert.EndsWith("&format=json", query);
    }

    // --- gate ------------------------------------------------------------------------------

    [Fact]
    public void Gate_PauseDoublesUpToTheMaximum_AndSuccessResetsIt()
    {
        var gate = new LastFmRequestGate(TestOptions.Create());

        gate.Pause();
        Assert.NotNull(gate.PausedUntil);
        Assert.Equal(TimeSpan.FromSeconds(2), gate.NextPause);
        gate.Pause();
        gate.Pause();
        Assert.Equal(TimeSpan.FromSeconds(4), gate.NextPause); // MaxRateLimitPauseSeconds

        gate.Succeeded();
        Assert.Equal(TimeSpan.FromSeconds(1), gate.NextPause);
    }
}
