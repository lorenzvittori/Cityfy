using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ServiceDefault.Listening;

/// <summary>
/// Writes plays into 'listening_events' so that the same play is one document whatever the source:
/// - same <see cref="ListeningEvent.Id"/>: upsert, the sources are merged and the known fields filled;
/// - a new id, but a play of the user from <b>another</b> source with the same
///   <see cref="ListeningEvent.MatchKey"/> and a start within <see cref="ListeningEvent.MatchTolerance"/>:
///   merged into that play (the first one arrived stays, the second only adds its source and fields).
/// Plays of the same source are never merged with each other: a short track played twice in a row
/// stays two plays. Used by the upsert of the sources and by the change of owner.
/// </summary>
internal sealed class ListeningEventWriter
{
    private readonly IMongoCollection<ListeningEvent> _events;
    private readonly Lazy<Task> _indexes;

    public ListeningEventWriter(IMongoCollection<ListeningEvent> events)
    {
        _events = events;
        _indexes = new Lazy<Task>(() => EnsureIndexesAsync(events));
    }

    /// <summary>Writes the plays with the sources they carry; returns how many were new.</summary>
    public async Task<int> WriteAsync(IReadOnlyCollection<ListeningEvent> events, CancellationToken ct)
    {
        await _indexes.Value;

        // Same play twice in one batch: keep one, otherwise the unordered bulk counts it twice
        var plays = events.Where(IsStorable).GroupBy(IdOf).Select(Prepared).ToList();
        if (plays.Count == 0)
            return 0;

        var mergeInto = await FindCrossSourceMatchesAsync(plays, ct);
        var now = DateTime.UtcNow;
        var writes = plays.Select(e => WriteOf(e, mergeInto, now)).ToList();

        var result = await _events.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
        return result.Upserts.Count;
    }

    private static bool IsStorable(ListeningEvent e) => !string.IsNullOrEmpty(e.UserId) && !string.IsNullOrEmpty(e.TrackUri);

    private static string IdOf(ListeningEvent e) => ListeningEvent.BuildId(e.UserId, e.TrackUri, e.PlayedAt);

    /// <summary>The first play of a group with the same id, with its id and match key set.</summary>
    private static ListeningEvent Prepared(IGrouping<string, ListeningEvent> sameId)
    {
        var e = sameId.First();
        e.Id = sameId.Key;
        e.MatchKey ??= ListeningEvent.BuildMatchKey(e.ArtistName, e.TrackName);
        return e;
    }

    /// <summary>Merge into the play of another source it duplicates, otherwise upsert under its own id.</summary>
    private static WriteModel<ListeningEvent> WriteOf(ListeningEvent e, Dictionary<string, string> mergeInto, DateTime now)
    {
        var filter = Builders<ListeningEvent>.Filter;
        if (mergeInto.TryGetValue(e.Id, out var existingId))
            return new UpdateOneModel<ListeningEvent>(filter.Eq(x => x.Id, existingId), Merge(e));
        return new UpdateOneModel<ListeningEvent>(filter.Eq(x => x.Id, e.Id), Insert(e, now)) { IsUpsert = true };
    }

    /// <summary>Incoming id → id of the play of another source it duplicates.</summary>
    private async Task<Dictionary<string, string>> FindCrossSourceMatchesAsync(List<ListeningEvent> plays, CancellationToken ct)
    {
        var mergeInto = new Dictionary<string, string>();
        var tolerance = ListeningEvent.MatchTolerance;

        foreach (var user in plays.Where(IsMatchable).GroupBy(UserIdOf))
        {
            var mine = user.ToList();
            var ids = mine.Select(e => e.Id).ToList();
            var keys = mine.Select(e => e.MatchKey!).Distinct().ToList();
            var from = mine.Min(e => e.StartedAt!.Value) - tolerance;
            var to = mine.Max(e => e.StartedAt!.Value) + tolerance;

            var f = Builders<ListeningEvent>.Filter;
            var candidates = await _events
                .Find(f.Eq(e => e.UserId, user.Key) & f.In(e => e.MatchKey, keys) & f.Gte(e => e.StartedAt, from) & f.Lte(e => e.StartedAt, to))
                .Project<Candidate>(Builders<ListeningEvent>.Projection
                    .Include(e => e.MatchKey).Include(e => e.StartedAt).Include(e => e.Sources))
                .ToListAsync(ct);
            if (candidates.Count == 0)
                continue;

            // A play already stored with its own id is a normal upsert, not a duplicate
            var stored = (await _events.Find(f.In(e => e.Id, ids)).Project(e => e.Id).ToListAsync(ct)).ToHashSet();
            var claimed = new HashSet<string>();
            var byKey = candidates.ToLookup(c => c.MatchKey);

            foreach (var e in mine.OrderBy(StartedAtOf))
            {
                if (stored.Contains(e.Id))
                    continue;
                if (Closest(byKey[e.MatchKey!], e, claimed, tolerance) is not { } match)
                    continue;
                mergeInto[e.Id] = match.Id;
                claimed.Add(match.Id);
            }
        }
        return mergeInto;
    }

    private static bool IsMatchable(ListeningEvent e) => e.MatchKey is not null && e.StartedAt is not null;

    private static string UserIdOf(ListeningEvent e) => e.UserId;

    private static DateTime? StartedAtOf(ListeningEvent e) => e.StartedAt;

    /// <summary>
    /// The candidate of another source, not yet claimed, whose start is the closest to the play
    /// and within the tolerance (the first one on a tie, as stored); null when none.
    /// </summary>
    private static Candidate? Closest(IEnumerable<Candidate> sameKey, ListeningEvent e, HashSet<string> claimed, TimeSpan tolerance)
    {
        Candidate? best = null;
        var bestDistance = TimeSpan.MaxValue;
        foreach (var c in sameKey)
        {
            if (c.Id == e.Id || claimed.Contains(c.Id) || c.Sources.Intersect(e.Sources).Any())
                continue;
            var distance = (c.StartedAt - e.StartedAt!.Value).Duration();
            if (distance <= tolerance && distance < bestDistance)
            {
                best = c;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static UpdateDefinition<ListeningEvent> Insert(ListeningEvent e, DateTime now)
    {
        var u = Builders<ListeningEvent>.Update;
        var updates = new List<UpdateDefinition<ListeningEvent>>
        {
            u.SetOnInsert(x => x.UserId, e.UserId),
            u.SetOnInsert(x => x.PlayedAt, e.PlayedAt.ToUniversalTime()),
            u.SetOnInsert(x => x.TrackUri, e.TrackUri),
            u.SetOnInsert(x => x.SourceRef, e.SourceRef),
            u.SetOnInsert(x => x.ImportedAt, e.ImportedAt == default ? now : e.ImportedAt),
        };
        updates.AddRange(Fields(e));
        return u.Combine(updates);
    }

    /// <summary>Into the play already there: its key, time and id stay; the new source and its fields are added.</summary>
    private static UpdateDefinition<ListeningEvent> Merge(ListeningEvent e) =>
        Builders<ListeningEvent>.Update.Combine(Fields(e, onlyWhenPresent: true));

    /// <summary>
    /// Fields only some sources know: set when present (the richer source wins), otherwise only
    /// initialised on insert.
    /// </summary>
    private static IEnumerable<UpdateDefinition<ListeningEvent>> Fields(ListeningEvent e, bool onlyWhenPresent = false)
    {
        var u = Builders<ListeningEvent>.Update;
        var updates = new List<UpdateDefinition<ListeningEvent>> { u.AddToSetEach(x => x.Sources, e.Sources) };
        void Add<T>(Expression<Func<ListeningEvent, T>> field, T value)
        {
            if (value is not null)
                updates.Add(u.Set(field, value));
            else if (!onlyWhenPresent)
                updates.Add(u.SetOnInsert(field, value));
        }
        Add(x => x.MsPlayed, e.MsPlayed);
        Add(x => x.Skipped, e.Skipped);
        Add(x => x.TrackName, e.TrackName);
        Add(x => x.ArtistName, e.ArtistName);
        Add(x => x.ArtistUri, e.ArtistUri);
        Add(x => x.AlbumName, e.AlbumName);
        Add(x => x.ContextUri, e.ContextUri);
        Add(x => x.StartedAt, e.StartedAt);
        Add(x => x.MatchKey, e.MatchKey);
        Add(x => x.ArtistMbid, e.ArtistMbid);
        Add(x => x.TrackMbid, e.TrackMbid);
        return updates;
    }

    private static Task EnsureIndexesAsync(IMongoCollection<ListeningEvent> collection)
    {
        var keys = Builders<ListeningEvent>.IndexKeys;
        return collection.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<ListeningEvent>(keys.Ascending(x => x.UserId).Descending(x => x.PlayedAt)),
            new CreateIndexModel<ListeningEvent>(keys.Ascending(x => x.UserId).Ascending(x => x.ArtistName)),
            // cross-source dedup
            new CreateIndexModel<ListeningEvent>(keys.Ascending(x => x.UserId).Ascending(x => x.MatchKey).Ascending(x => x.StartedAt)),
        ]);
    }

    private sealed class Candidate
    {
        public string Id { get; set; } = null!;
        public string MatchKey { get; set; } = null!;
        [MongoDB.Bson.Serialization.Attributes.BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime StartedAt { get; set; }
        public List<string> Sources { get; set; } = new();
    }
}
