using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CityFy.Retrieve.LastFm.Clients;
using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Options;
using CityFy.Retrieve.LastFm.Repositories;
using ServiceDefault.Errors;
using ServiceDefault.Listening;
using ServiceDefault.Messaging;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Tests;

/// <summary>A user's Last.fm history in memory: answers getRecentTracks like Last.fm (newest first, pages, bounds).</summary>
internal sealed class FakeLastFmClient : ILastFmClient
{
    public List<LastFmScrobble> History { get; } = new();
    public LastFmNowPlaying? NowPlaying { get; set; }
    public int PageSize { get; set; } = 2;
    public LastFmSession Session { get; set; } = new("alice", "session-key");

    /// <summary>Thrown by the next getRecentTracks calls (one per call, in order).</summary>
    public Queue<Exception> Failures { get; } = new();

    public List<(DateTime? From, DateTime? To, int Page)> Calls { get; } = new();

    public string GetAuthorizeUrl(string callbackUrl) => $"https://last.fm/auth?cb={callbackUrl}";

    public Task<LastFmSession> GetSessionAsync(string token, CancellationToken ct) =>
        token == "bad" ? throw new LastFmApiException(LastFmApiException.AuthenticationFailed, "Invalid token") : Task.FromResult(Session);

    public Task<LastFmRecentTracksPage> GetRecentTracksAsync(string username, string? sessionKey, DateTime? from, DateTime? to, int page, CancellationToken ct)
    {
        Calls.Add((from, to, page));
        if (Failures.TryDequeue(out var failure))
            throw failure;

        var matching = History
            .Where(s => (from is null || s.ScrobbledAt > from) && (to is null || s.ScrobbledAt < to))
            .OrderByDescending(s => s.ScrobbledAt)
            .ToList();
        var totalPages = (int)Math.Ceiling(matching.Count / (double)PageSize);
        var items = matching.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        return Task.FromResult(new LastFmRecentTracksPage(items, page == 1 ? NowPlaying : null, page, totalPages, matching.Count));
    }

    public static LastFmScrobble Scrobble(DateTime at, string artist = "Artist", string? track = null) =>
        new(artist, track ?? $"Track {at:HHmmss}", "Album", null, null, at);
}

internal sealed class InMemoryListeningEventStore : IListeningEventStore
{
    public ConcurrentDictionary<string, ListeningEvent> Events { get; } = new();

    public Task<int> UpsertAsync(IReadOnlyCollection<ListeningEvent> events, string source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var inserted = events.Count(e => Events.TryAdd(ListeningEvent.BuildId(e.UserId, e.TrackUri, e.PlayedAt), e));
        return Task.FromResult(inserted);
    }
}

internal sealed class FakePublisher : IMessagePublisher
{
    public List<object> Published { get; } = new();

    public Task PublishAsync<T>(T message, string? correlationId = null, CancellationToken ct = default) where T : class
    {
        Published.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryLastFmAccountRepository : ILastFmAccountRepository
{
    public ConcurrentDictionary<string, LastFmAccount> Accounts { get; } = new();

    public Task<LastFmAccount?> GetAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Accounts.TryGetValue(userId, out var a) ? a : null);

    public Task<LastFmAccount?> GetByUsernameAsync(string username, CancellationToken ct) =>
        Task.FromResult(Accounts.Values.FirstOrDefault(a => a.Username == username));

    public Task<IReadOnlyList<LastFmAccount>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LastFmAccount>>(Accounts.Values.OrderBy(a => a.Username).ToList());

    public Task UpsertAsync(LastFmAccount account, CancellationToken ct)
    {
        Accounts[account.UserId] = account;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LastFmAccount>> ListDueAsync(DateTime now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LastFmAccount>>(Accounts.Values
            .Where(a => a.Status == LastFmAccountStatus.Active && a.NextPollAt <= now)
            .OrderBy(a => a.NextPollAt).Take(limit).ToList());

    public Task UpdatePollAsync(string userId, DateTime polledAt, DateTime lastScrobbleAt, DateTime? lastListeningAt, DateTime nextPollAt, CancellationToken ct)
    {
        if (Accounts.TryGetValue(userId, out var a))
        {
            a.LastPolledAt = polledAt;
            a.NextPollAt = nextPollAt;
            if (lastScrobbleAt > a.LastScrobbleAt) a.LastScrobbleAt = lastScrobbleAt;
            if (lastListeningAt is { } l && (a.LastListeningAt is null || l > a.LastListeningAt)) a.LastListeningAt = l;
        }
        return Task.CompletedTask;
    }

    public Task SuspendAsync(string userId, string errorCode, CancellationToken ct)
    {
        if (Accounts.TryGetValue(userId, out var a))
        {
            a.Status = LastFmAccountStatus.Suspended;
            a.ErrorCode = errorCode;
        }
        return Task.CompletedTask;
    }

    public Task SetNextPollAsync(string userId, DateTime nextPollAt, CancellationToken ct)
    {
        if (Accounts.TryGetValue(userId, out var a)) a.NextPollAt = nextPollAt;
        return Task.CompletedTask;
    }

    public Task PullNextPollAsync(string userId, DateTime at, CancellationToken ct)
    {
        if (Accounts.TryGetValue(userId, out var a) && at < a.NextPollAt) a.NextPollAt = at;
        return Task.CompletedTask;
    }

    public Task SetHistoryImportAsync(string userId, LastFmHistoryImport? import, CancellationToken ct)
    {
        if (Accounts.TryGetValue(userId, out var a)) a.HistoryImport = import;
        return Task.CompletedTask;
    }

    public Task<long> DeleteAsync(string userId, CancellationToken ct) => Task.FromResult(Accounts.TryRemove(userId, out _) ? 1L : 0L);

    public Task<long> DeleteAllAsync(CancellationToken ct)
    {
        var count = Accounts.Count;
        Accounts.Clear();
        return Task.FromResult((long)count);
    }
}

internal sealed class InMemoryImportTaskRepository : ILastFmImportTaskRepository
{
    public ConcurrentDictionary<string, ProcessingTask> Tasks { get; } = new();

    public Task CreateAsync(string importId, string userId, CancellationToken ct)
    {
        Tasks[importId] = new ProcessingTask { UploadId = importId, UserId = userId, Status = ProcessingStatus.Pending };
        return Task.CompletedTask;
    }

    public Task<ProcessingTask?> GetAsync(string importId, CancellationToken ct) =>
        Task.FromResult(Tasks.TryGetValue(importId, out var t) ? t : null);

    public Task<IReadOnlyList<ProcessingTask>> ListAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProcessingTask>>(Tasks.Values.OrderByDescending(t => t.StartedAt).Take(limit).ToList());

    public Task<IReadOnlyList<ProcessingTask>> ListUnfinishedAsync(string? userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProcessingTask>>(Tasks.Values
            .Where(t => t.Status is ProcessingStatus.Pending or ProcessingStatus.InProgress && (userId is null || t.UserId == userId))
            .ToList());

    public Task UpdateStatusAsync(string importId, ProcessingStatus status, int? inserted = null, string? message = null, string? errorCode = null)
    {
        if (Tasks.TryGetValue(importId, out var t) && (status == ProcessingStatus.Cancelled || t.Status != ProcessingStatus.Cancelled))
        {
            t.Status = status;
            t.ErrorCode = status switch
            {
                ProcessingStatus.Failed => errorCode ?? ErrorCodes.LastFmImportFailed,
                ProcessingStatus.Cancelled => ErrorCodes.ProcessCancelled,
                _ => null
            };
            if (inserted.HasValue) t.Inserted = inserted.Value;
            if (message != null) t.Message = message;
        }
        return Task.CompletedTask;
    }

    public Task UpdateProgressAsync(string importId, double? percent, string? detail, int inserted)
    {
        if (Tasks.TryGetValue(importId, out var t) && t.Status == ProcessingStatus.InProgress)
        {
            t.ProgressPercent = percent;
            t.ProgressDetail = detail;
            t.Inserted = inserted;
        }
        return Task.CompletedTask;
    }

    public Task<long> DeleteAllAsync(string userId, CancellationToken ct)
    {
        var mine = Tasks.Values.Where(t => t.UserId == userId).Select(t => t.UploadId).ToList();
        foreach (var id in mine) Tasks.TryRemove(id, out _);
        return Task.FromResult((long)mine.Count);
    }

    public Task<long> DeleteEverythingAsync(CancellationToken ct)
    {
        var count = Tasks.Count;
        Tasks.Clear();
        return Task.FromResult((long)count);
    }
}

/// <summary>Returns queued responses in order and records the requests.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHttpHandler Respond(HttpStatusCode status, string json)
    {
        _responses.Enqueue((status, json));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (!_responses.TryDequeue(out var r))
            throw new InvalidOperationException($"Unexpected request {request.RequestUri}");
        return Task.FromResult(new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") });
    }
}

internal static class TestOptions
{
    /// <summary>Fast options for the tests (short pauses, small pages).</summary>
    public static Microsoft.Extensions.Options.IOptions<LastFmOptions> Create(Action<LastFmOptions>? configure = null)
    {
        var o = new LastFmOptions
        {
            ApiKey = "key",
            SharedSecret = "secret",
            CallbackUrl = "http://127.0.0.1:4200/api/v1/auth/lastfm/callback",
            MaxRequestsPerSecond = 20,
            RateLimitPauseSeconds = 1,
            MaxRateLimitPauseSeconds = 4,
            MaxAttempts = 3,
            MaxPollPages = 2
        };
        configure?.Invoke(o);
        return Microsoft.Extensions.Options.Options.Create(o);
    }
}
