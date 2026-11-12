using System.Net;
using System.Net.Http.Headers;
using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.Tests;

public sealed class SpotifyClientTests
{
    private const string RecentlyPlayedPage = """
        {
          "items": [
            {
              "track": {
                "id": "t1", "name": "Song", "uri": "spotify:track:t1",
                "duration_ms": 215000, "track_number": 3, "is_local": false,
                "artists": [ { "id": "a1", "name": "Artist", "uri": "spotify:artist:a1" } ],
                "album": { "id": "al1", "name": "Album", "release_date": "2020-01-01" }
              },
              "played_at": "2026-11-10T21:15:30.123Z",
              "context": { "type": "playlist", "uri": "spotify:playlist:p1" }
            }
          ],
          "next": "https://api.spotify.com/v1/me/player/recently-played?before=1762809330123&limit=50",
          "limit": 50
        }
        """;

    private static (SpotifyClient Client, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var client = new SpotifyClient(new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new SpotifyOptions()), NullLogger<SpotifyClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task RecentlyPlayed_DeserializesSnakeCaseFields()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, RecentlyPlayedPage);

        var page = await client.GetRecentlyPlayedPageAsync("token", null, null, 50, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(215000, item.Track!.DurationMs);
        Assert.Equal(3, item.Track.TrackNumber);
        Assert.Equal("2020-01-01", item.Track.Album!.ReleaseDate);
        Assert.Equal(new DateTime(2026, 11, 10, 21, 15, 30, 123, DateTimeKind.Utc), item.PlayedAt.ToUniversalTime());
        Assert.Equal("spotify:playlist:p1", item.Context!.Uri);
    }

    [Fact]
    public async Task RecentlyPlayed_ReadsTheBeforeCursorFromNext()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, RecentlyPlayedPage);

        var page = await client.GetRecentlyPlayedPageAsync("token", null, null, 50, CancellationToken.None);

        Assert.Equal(1762809330123, page.Before);
    }

    [Fact]
    public async Task RecentlyPlayed_UsesAfter_WhenGiven_AndSendsTheBearerToken()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.OK, """{ "items": [], "next": null }""");

        await client.GetRecentlyPlayedPageAsync("Bearer abc", before: 5, after: 42, limit: 10, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("after=42", request.RequestUri!.Query);
        Assert.DoesNotContain("before=", request.RequestUri.Query);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "abc"), request.Headers.Authorization);
    }

    [Fact]
    public async Task TooManyRequests_IsRetriedAfterRetryAfter()
    {
        var (client, handler) = Create();
        handler
            .Respond(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero))
            .Respond(HttpStatusCode.OK, """{ "id": "user1", "display_name": "User" }""");

        var user = await client.GetCurrentUserAsync("token", CancellationToken.None);

        Assert.Equal("user1", user.Id);
        Assert.Equal("User", user.DisplayName);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ClientErrors_AreNotRetried_AndThrow()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.Unauthorized, """{ "error": { "status": 401 } }""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCurrentUserAsync("expired", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetryAfterTooLong_FailsInsteadOfWaiting()
    {
        var (client, handler) = Create();
        handler.Respond(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCurrentUserAsync("token", CancellationToken.None));
        Assert.Single(handler.Requests);
    }
}
