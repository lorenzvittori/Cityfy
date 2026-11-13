using MongoDB.Driver;
using ServiceDefault.Listening;
using Testcontainers.MongoDb;

namespace ServiceDefault.Tests;

/// <summary>
/// Integration tests on a real MongoDB (Testcontainers). Skipped when Docker is not available.
/// </summary>
public sealed class MongoListeningEventStoreTests : IAsyncLifetime
{
    private MongoDbContainer? _container;
    private string? _dockerUnavailable;
    private IMongoDatabase _db = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder("mongo:8.0").Build();
            await _container.StartAsync();
            _db = new MongoClient(_container.GetConnectionString()).GetDatabase($"test_{Guid.NewGuid():N}");
        }
        catch (Exception ex)
        {
            _dockerUnavailable = ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }

    private MongoListeningEventStore CreateStore()
    {
        Assert.SkipWhen(_dockerUnavailable != null, $"Docker not available: {_dockerUnavailable}");
        return new MongoListeningEventStore(_db);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime At = new(2026, 11, 10, 21, 15, 30, DateTimeKind.Utc);

    private static ListeningEvent Play(DateTime at, int? msPlayed = null, string? artistUri = null) => new()
    {
        UserId = "user1",
        TrackUri = "spotify:track:t1",
        PlayedAt = at,
        MsPlayed = msPlayed,
        ArtistName = "Artist",
        ArtistUri = artistUri
    };

    [Fact]
    public async Task Upsert_IsIdempotent()
    {
        var store = CreateStore();

        var first = await store.UpsertAsync([Play(At, 1000)], ListeningSources.Dump, Ct);
        var second = await store.UpsertAsync([Play(At, 1000)], ListeningSources.Dump, Ct);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, await _db.GetCollection<ListeningEvent>(ListeningEvent.CollectionName).CountDocumentsAsync(FilterDefinition<ListeningEvent>.Empty, cancellationToken: Ct));
    }

    [Fact]
    public async Task SameId_FromTwoSources_IsMerged_AndKeepsTheRicherFields()
    {
        var store = CreateStore();

        // same track key and second (milliseconds are truncated): one play, whoever wrote it first
        await store.UpsertAsync([Play(At.AddMilliseconds(456), artistUri: "spotify:artist:a1")], ListeningSources.LastFm, Ct);
        var newFromDump = await store.UpsertAsync([Play(At, msPlayed: 185000)], ListeningSources.Dump, Ct);

        Assert.Equal(0, newFromDump);
        var doc = await _db.GetCollection<ListeningEvent>(ListeningEvent.CollectionName).Find(FilterDefinition<ListeningEvent>.Empty).SingleAsync(Ct);
        Assert.Equal(185000, doc.MsPlayed);                    // from the dump
        Assert.Equal("spotify:artist:a1", doc.ArtistUri);     // from the first source, not overwritten by null
        Assert.Equal([ListeningSources.LastFm, ListeningSources.Dump], doc.Sources);
    }

    [Fact]
    public async Task Transfer_MovesEveryPlayToTheNewOwner_MergingTheOnesItAlreadyHas()
    {
        var store = CreateStore();
        var transfer = new MongoListeningEventTransfer(_db);
        var events = _db.GetCollection<ListeningEvent>(ListeningEvent.CollectionName);
        // anon: two plays (one also known by the account, from another source); account: that one
        await store.UpsertAsync([Owned(Play(At, 1000), "anon_s"), Owned(Play(At.AddMinutes(5), 2000), "anon_s")], ListeningSources.Dump, Ct);
        await store.UpsertAsync([Owned(Play(At), "u1")], ListeningSources.LastFm, Ct);

        var moved = await transfer.TransferAsync("anon_s", "u1", Ct);
        var again = await transfer.TransferAsync("anon_s", "u1", Ct);

        Assert.Equal((2L, 0L), (moved, again));
        Assert.Equal(0, await events.CountDocumentsAsync(e => e.UserId == "anon_s", cancellationToken: Ct));
        var mine = await events.Find(e => e.UserId == "u1").SortBy(e => e.PlayedAt).ToListAsync(Ct);
        Assert.Equal(2, mine.Count);
        Assert.Equal([ListeningSources.LastFm, ListeningSources.Dump], mine[0].Sources);
        Assert.Equal(ListeningEvent.BuildId("u1", "spotify:track:t1", At.AddMinutes(5)), mine[1].Id);
        Assert.Equal(2000, mine[1].MsPlayed);
    }

    // --- cross-source dedup (PIANO_LASTFM.md L3) ---------------------------------------------

    /// <summary>A play of "Muse – Uprising" from the export: 'ts' is the end, the start is ts - ms_played.</summary>
    private static ListeningEvent FromExport(DateTime start, int seconds = 300, string userId = "user1") => new()
    {
        UserId = userId,
        TrackUri = "spotify:track:uprising",
        PlayedAt = start.AddSeconds(seconds),
        StartedAt = start,
        MsPlayed = seconds * 1000,
        ArtistName = "Muse",
        TrackName = "Uprising"
    };

    /// <summary>The same song as a Last.fm scrobble (scrobble time = start), with the artist MBID.</summary>
    private static ListeningEvent FromLastFm(DateTime start, string userId = "user1") => new()
    {
        UserId = userId,
        TrackUri = "lastfm:muse|uprising",
        PlayedAt = start,
        StartedAt = start,
        ArtistName = "Muse",
        TrackName = "Uprising",
        ArtistMbid = "9c9f1380-2516-4fc9-a3e6-f9f61941d090"
    };

    private Task<List<ListeningEvent>> AllAsync(string userId = "user1") =>
        _db.GetCollection<ListeningEvent>(ListeningEvent.CollectionName).Find(e => e.UserId == userId).SortBy(e => e.StartedAt).ToListAsync(Ct);

    [Fact]
    public async Task SamePlay_FromTheExportThenLastFm_IsOnePlay_WithBothSourcesAndTheMbid()
    {
        var store = CreateStore();

        var fromExport = await store.UpsertAsync([FromExport(At)], ListeningSources.Dump, Ct);
        var fromLastFm = await store.UpsertAsync([FromLastFm(At.AddSeconds(40))], ListeningSources.LastFm, Ct);

        Assert.Equal((1, 0), (fromExport, fromLastFm));
        var play = Assert.Single(await AllAsync());
        Assert.Equal([ListeningSources.Dump, ListeningSources.LastFm], play.Sources);
        Assert.Equal("spotify:track:uprising", play.TrackUri); // the first one arrived stays
        Assert.Equal(300_000, play.MsPlayed);
        Assert.Equal("9c9f1380-2516-4fc9-a3e6-f9f61941d090", play.ArtistMbid);
    }

    [Fact]
    public async Task SamePlay_FromLastFmThenTheExport_IsOnePlay_WithTheDurationOfTheExport()
    {
        var store = CreateStore();

        await store.UpsertAsync([FromLastFm(At)], ListeningSources.LastFm, Ct);
        var fromExport = await store.UpsertAsync([FromExport(At.AddMinutes(-1))], ListeningSources.Dump, Ct);

        Assert.Equal(0, fromExport);
        var play = Assert.Single(await AllAsync());
        Assert.Equal(300_000, play.MsPlayed);
        Assert.Equal([ListeningSources.LastFm, ListeningSources.Dump], play.Sources);
    }

    [Fact]
    public async Task SameSong_MoreThanTwoMinutesApart_IsTwoPlays()
    {
        var store = CreateStore();

        await store.UpsertAsync([FromExport(At)], ListeningSources.Dump, Ct);
        await store.UpsertAsync([FromLastFm(At.AddMinutes(2).AddSeconds(1))], ListeningSources.LastFm, Ct);

        Assert.Equal(2, (await AllAsync()).Count);
    }

    [Fact]
    public async Task SameSong_PlayedTwiceInARow_FromOneSource_IsNeverMerged_AndLastFmJoinsTheClosest()
    {
        var store = CreateStore();

        // a short song twice in a row in the export, then the scrobble of the second one
        await store.UpsertAsync([FromExport(At, seconds: 50), FromExport(At.AddSeconds(55), seconds: 50)], ListeningSources.Dump, Ct);
        await store.UpsertAsync([FromLastFm(At.AddSeconds(60))], ListeningSources.LastFm, Ct);

        var plays = await AllAsync();
        Assert.Equal(2, plays.Count);
        Assert.Equal([ListeningSources.Dump], plays[0].Sources);
        Assert.Equal([ListeningSources.Dump, ListeningSources.LastFm], plays[1].Sources);
    }

    [Fact]
    public async Task Transfer_MergesThePlaysTheAccountAlreadyHasFromAnotherSource()
    {
        var store = CreateStore();
        var transfer = new MongoListeningEventTransfer(_db);
        await store.UpsertAsync([FromExport(At, userId: "anon_s")], ListeningSources.Dump, Ct);
        await store.UpsertAsync([FromLastFm(At.AddSeconds(30), userId: "u1")], ListeningSources.LastFm, Ct);

        await transfer.TransferAsync("anon_s", "u1", Ct);

        var play = Assert.Single(await AllAsync("u1"));
        Assert.Equal([ListeningSources.LastFm, ListeningSources.Dump], play.Sources);
        Assert.Equal(300_000, play.MsPlayed);
        Assert.Empty(await AllAsync("anon_s"));
    }

    private static ListeningEvent Owned(ListeningEvent e, string userId)
    {
        e.UserId = userId;
        return e;
    }

    [Fact]
    public async Task DuplicatesInsideOneBatch_CountOnce()
    {
        var store = CreateStore();

        var inserted = await store.UpsertAsync([Play(At, 1000), Play(At, 1000)], ListeningSources.Dump, Ct);

        Assert.Equal(1, inserted);
    }
}
