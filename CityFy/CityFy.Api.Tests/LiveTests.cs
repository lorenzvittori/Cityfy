using System.Net;
using System.Text.Json;
using CityFy.Api.Models;
using CityFy.Api.Services;
using CityFy.Api.SubServices;
using Microsoft.Extensions.DependencyInjection;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;

namespace CityFy.Api.Tests;

/// <summary>api/v1/live: the track playing now, pushed to the open pages (Server-Sent Events).</summary>
public sealed class LiveTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    /// <summary>An account whose Last.fm says it is listening to Radiohead – Reckoner.</summary>
    private async Task<(HttpClient Client, string UserId)> ListeningAccountAsync()
    {
        _factory.FakeLastFmLink();
        var client = _factory.CreateApiClient();
        await _factory.LastFmCallbackAsync(client);
        var userId = Assert.Single(_factory.Users.Users.Keys);
        _factory.Fakes.OnJson(HttpMethod.Get, $"/internal/lastfm/accounts/{userId}", new
        {
            userId, username = "alice", status = "Active", linkedAt = DateTime.UtcNow, lastScrobbleAt = DateTime.UtcNow, nextPollAt = DateTime.UtcNow,
            nowPlaying = new { artist = "Radiohead", track = "Reckoner", album = "In Rainbows", artistMbid = "a74b1b7f", seenAt = DateTime.UtcNow }
        });
        return (client, userId);
    }

    /// <summary>Next "event: … / data: …" of the stream.</summary>
    private static async Task<(string Event, JsonElement Data)> NextEventAsync(StreamReader reader)
    {
        string? type = null;
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
                type = line["event: ".Length..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
                return (type ?? "message", JsonDocument.Parse(line["data: ".Length..]).RootElement);
        }
        throw new InvalidOperationException("stream ended");
    }

    [Fact]
    public async Task Live_SendsTheTrackPlayingNow_ThenEveryChange_AndTellsLastFmThePageIsOpen()
    {
        var (client, userId) = await ListeningAccountAsync();

        using var resp = await client.GetAsync("/api/v1/live", HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(Ct));

        var (type, first) = await NextEventAsync(reader);
        Assert.Equal("now-playing", type);
        Assert.True(first.GetProperty("playing").GetBoolean());
        Assert.Equal("Reckoner", first.GetProperty("track").GetString());
        Assert.Equal("a74b1b7f", first.GetProperty("artistMbid").GetString());
        Assert.Equal("https://www.last.fm/music/Radiohead/_/Reckoner", first.GetProperty("trackUrl").GetString());
        Assert.Contains(_factory.Published.Messages, m => m is UserActive a && a.UserId == userId);

        // Retrieve.LastFm sees that the music stopped
        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ILiveService>()
                .OnNowPlayingChangedAsync(new NowPlayingChanged(userId, null, null, null, null, null, DateTime.UtcNow), Ct);

        var (_, second) = await NextEventAsync(reader);
        Assert.False(second.GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task Live_WithoutAnAccount_IsNotAvailable()
    {
        var (client, _) = await _factory.StartAnonymousAsync();

        var resp = await client.GetAsync("/api/v1/live", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(ErrorCodes.LastFmNotLinked, JsonDocument.Parse(await resp.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void Hub_SendsToEveryPageOfTheUser_AndLimitsThePagesPerUser()
    {
        var hub = new LiveHubSubService();
        var pages = Enumerable.Range(0, LiveHubSubService.MaxConnectionsPerUser).Select(_ => hub.TrySubscribe("u1")).ToList();

        Assert.Null(hub.TrySubscribe("u1"));
        Assert.NotNull(hub.TrySubscribe("u2"));

        hub.Publish("u1", NowPlayingDto.Nothing);
        Assert.All(pages, p => Assert.True(p!.Events.TryRead(out _)));

        pages[0]!.Dispose();
        Assert.NotNull(hub.TrySubscribe("u1")); // a closed page frees its place
    }

    [Fact]
    public async Task OldChange_IsDropped()
    {
        var hub = new LiveHubSubService();
        using var page = hub.TrySubscribe("u1")!;
        var live = new LiveService(hub, null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveService>.Instance);

        await live.OnNowPlayingChangedAsync(new NowPlayingChanged("u1", "A", "B", null, null, null, DateTime.UtcNow.AddMinutes(-10)), Ct);

        Assert.False(page.Events.TryRead(out _));
    }
}
