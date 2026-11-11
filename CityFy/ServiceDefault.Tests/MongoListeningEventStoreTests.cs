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
    public async Task SamePlay_FromApiThenDump_IsMerged_AndKeepsTheRicherFields()
    {
        var store = CreateStore();

        await store.UpsertAsync([Play(At.AddMilliseconds(456), artistUri: "spotify:artist:a1")], ListeningSources.Api, Ct);
        var newFromDump = await store.UpsertAsync([Play(At, msPlayed: 185000)], ListeningSources.Dump, Ct);

        Assert.Equal(0, newFromDump);
        var doc = await _db.GetCollection<ListeningEvent>(ListeningEvent.CollectionName).Find(FilterDefinition<ListeningEvent>.Empty).SingleAsync(Ct);
        Assert.Equal(185000, doc.MsPlayed);                    // from the dump
        Assert.Equal("spotify:artist:a1", doc.ArtistUri);     // from the API, not overwritten by null
        Assert.Equal([ListeningSources.Api, ListeningSources.Dump], doc.Sources);
    }

    [Fact]
    public async Task DuplicatesInsideOneBatch_CountOnce()
    {
        var store = CreateStore();

        var inserted = await store.UpsertAsync([Play(At, 1000), Play(At, 1000)], ListeningSources.Dump, Ct);

        Assert.Equal(1, inserted);
    }
}
