using CityFy.Retrieve.LastFm.Mappers;
using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Services;
using CityFy.Retrieve.LastFm.SubServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceDefault.Contracts;
using ServiceDefault.Errors;
using ServiceDefault.Hosting;
using ServiceDefault.Listening;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Tests;

/// <summary>Link, history import, polling and deletion with a fake Last.fm and in-memory storage.</summary>
public sealed class LastFmFlowTests
{
    private const string UserId = "u1";
    /// <summary>In the past: an account linked at T0 is due for polling now.</summary>
    private static readonly DateTime T0 = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeLastFmClient _lastFm = new();
    private readonly InMemoryListeningEventStore _plays = new();
    private readonly FakePublisher _publisher = new();
    private readonly InMemoryLastFmAccountRepository _accountRepo = new();
    private readonly InMemoryImportTaskRepository _taskRepo = new();
    private readonly LastFmImportQueue _queue = new();
    private readonly CancellationRegistry _running = new();

    private readonly LastFmAccountStoreSubService _accounts;
    private readonly LastFmImportJobSubService _imports;
    private readonly NowPlayingStoreSubService _nowPlaying = new();
    private readonly LastFmScrobbleReadSubService _reader;
    private readonly LastFmPollCadenceSubService _cadence;
    private readonly ListeningImportedEventSubService _events;
    private readonly LiveEventSubService _live;
    private readonly UserActivitySubService _activity = new(TestOptions.Create());
    private readonly LastFmErrorSubService _errors = new();

    public LastFmFlowTests()
    {
        var options = TestOptions.Create();
        _accounts = new LastFmAccountStoreSubService(_accountRepo, new EphemeralDataProtectionProvider(), options);
        _imports = new LastFmImportJobSubService(_taskRepo, _queue, _running);
        _reader = new LastFmScrobbleReadSubService(_lastFm, new LastFmListeningMapper(), _plays, options);
        _cadence = new LastFmPollCadenceSubService(options);
        _events = new ListeningImportedEventSubService(_publisher);
        _live = new LiveEventSubService(_publisher);
    }

    private readonly LastFmAccountHintSubService _hints = new(TestOptions.Create());

    private LastFmAccountService AccountService() => new(
        new LastFmAuthSubService(_lastFm, TestOptions.Create()), _accounts, _imports, _nowPlaying, _activity, _hints, NullLogger<LastFmAccountService>.Instance);

    private UserDataService UserData() => new(_imports, _accounts, _nowPlaying, _activity, NullLogger<UserDataService>.Instance);

    private LastFmImportService ImportService() => new(_imports, _accounts, _reader, _events, _errors, NullLogger<LastFmImportService>.Instance);

    private LastFmPollService PollService() =>
        new(_accounts, _reader, _nowPlaying, _activity, _cadence, _events, _live, _errors, NullLogger<LastFmPollService>.Instance);

    private IEnumerable<NowPlayingChanged> NowPlayingEvents => _publisher.Published.OfType<NowPlayingChanged>();

    private LastFmAccount Account => _accountRepo.Accounts[UserId];

    /// <summary>Linked with the polling cursor at <paramref name="cursor"/> and no import running.</summary>
    private async Task<LastFmAccount> LinkedAsync(DateTime cursor)
    {
        var account = await _accounts.LinkAsync(UserId, _lastFm.Session, cursor, Ct);
        account.HistoryImport = new LastFmHistoryImport("done", cursor, 1, 0, 0);
        return account;
    }

    private void Scrobbles(params int[] minutesFromT0)
    {
        foreach (var m in minutesFromT0)
            _lastFm.History.Add(FakeLastFmClient.Scrobble(T0.AddMinutes(m)));
    }

    private async Task<LastFmImportJob> DequeueAsync()
    {
        await foreach (var job in _queue.ReadAllAsync(Ct))
            return job;
        throw new InvalidOperationException("empty queue");
    }

    // --- link ------------------------------------------------------------------------------

    [Fact]
    public async Task Link_StoresTheAccount_AndQueuesTheHistoryUpToTheLinkTime()
    {
        var result = await AccountService().LinkAsync(UserId, "token", Ct);

        Assert.Equal("alice", result.Username);
        Assert.NotNull(result.ImportId);
        Assert.Equal(LastFmAccountStatus.Active, Account.Status);
        Assert.NotEqual("session-key", Account.ProtectedSessionKey); // encrypted
        Assert.Equal("session-key", _accounts.GetSessionKey(Account));
        Assert.Equal(Account.LastScrobbleAt, Account.HistoryImport!.Before);
        Assert.Equal(result.ImportId, (await DequeueAsync()).ImportId);
    }

    [Fact]
    public async Task LinkAgain_SameAccount_KeepsTheCursor_AndDoesNotImportAgain()
    {
        await LinkedAsync(T0);
        Account.Status = LastFmAccountStatus.Suspended;

        var result = await AccountService().LinkAsync(UserId, "token", Ct);

        Assert.Null(result.ImportId);
        Assert.Equal(T0, Account.LastScrobbleAt);
        Assert.Equal(LastFmAccountStatus.Active, Account.Status);
    }

    [Fact]
    public async Task Link_AccountAlreadyOwned_LogsInItsOwner()
    {
        await _accounts.LinkAsync("owner", _lastFm.Session, T0, Ct);
        _accountRepo.Accounts["owner"].HistoryImport = new LastFmHistoryImport("done", T0, 1, 0, 0);

        var result = await AccountService().LinkAsync("new-user", "token", Ct);

        Assert.Equal("owner", result.UserId);
        Assert.Null(result.ImportId);
        Assert.Equal(["owner"], _accountRepo.Accounts.Keys);
    }

    [Fact]
    public async Task Link_TokenRefused_IsAuthFailed()
    {
        var ex = await Assert.ThrowsAsync<CityFyException>(() => AccountService().LinkAsync(UserId, "bad", Ct));

        Assert.Equal(ErrorCodes.LastFmAuthFailed, ex.Code);
        Assert.Empty(_accountRepo.Accounts);
    }

    // --- history import ----------------------------------------------------------------------

    [Fact]
    public async Task Import_ReadsEveryPageBeforeTheLink_ThenPublishes()
    {
        Scrobbles(-50, -40, -30, -20, -10, 5); // 5 before the link, 1 after (left to the polling)
        var importId = await AccountService().StartImportAsync((await LinkedAsync(T0)).UserId, Ct);

        await ImportService().RunAsync(await DequeueAsync(), Ct);

        var task = _taskRepo.Tasks[importId];
        Assert.Equal(ProcessingStatus.Done, task.Status);
        Assert.Equal(5, task.Inserted);
        Assert.Equal(5, _plays.Events.Count);
        Assert.All(_lastFm.Calls, c => Assert.Equal(T0, c.To)); // stable upper bound
        Assert.Equal([1, 2, 3], _lastFm.Calls.Select(c => c.Page));
        Assert.Equal(new LastFmHistoryImport(importId, T0, 4, 3, 5), Account.HistoryImport);
        var imported = Assert.IsType<ListeningImported>(Assert.Single(_publisher.Published));
        Assert.Equal((UserId, ListeningSources.LastFm, 5), (imported.UserId, imported.Source, imported.NewPlays));
    }

    [Fact]
    public async Task Import_ResumesFromTheSavedPage()
    {
        Scrobbles(-50, -40, -30, -20, -10);
        await LinkedAsync(T0);
        Account.HistoryImport = new LastFmHistoryImport("i1", T0, NextPage: 3, TotalPages: 3, Inserted: 4);
        await _imports.CreateAndEnqueueAsync("i1", UserId, Ct);

        await ImportService().RunAsync(await DequeueAsync(), Ct);

        Assert.Equal([3], _lastFm.Calls.Select(c => c.Page));
        Assert.Equal(5, _taskRepo.Tasks["i1"].Inserted); // 4 before the restart + 1
    }

    [Fact]
    public async Task Import_EmptyHistory_IsDoneWithoutEvent()
    {
        var importId = await AccountService().StartImportAsync((await LinkedAsync(T0)).UserId, Ct);

        await ImportService().RunAsync(await DequeueAsync(), Ct);

        Assert.Equal(ProcessingStatus.Done, _taskRepo.Tasks[importId].Status);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task Import_CancelledWhileQueued_DoesNotRun()
    {
        Scrobbles(-10);
        var importId = await AccountService().StartImportAsync((await LinkedAsync(T0)).UserId, Ct);
        await ImportService().CancelAsync(importId, Ct);

        await ImportService().RunAsync(await DequeueAsync(), Ct);

        Assert.Empty(_lastFm.Calls);
        Assert.Equal(ProcessingStatus.Cancelled, _taskRepo.Tasks[importId].Status);
    }

    [Fact]
    public async Task Import_PrivateProfile_FailsWithItsCode_AndSuspendsTheAccount()
    {
        var importId = await AccountService().StartImportAsync((await LinkedAsync(T0)).UserId, Ct);
        _lastFm.Failures.Enqueue(new LastFmApiException(LastFmApiException.LoginRequired, "Login: User required to be logged in"));

        await ImportService().RunAsync(await DequeueAsync(), Ct);

        Assert.Equal((ProcessingStatus.Failed, ErrorCodes.LastFmUserPrivate), (_taskRepo.Tasks[importId].Status, _taskRepo.Tasks[importId].ErrorCode));
        Assert.Equal((LastFmAccountStatus.Suspended, ErrorCodes.LastFmUserPrivate), (Account.Status, Account.ErrorCode));
    }

    [Fact]
    public async Task Recovery_QueuesAgainTheUnfinishedImports()
    {
        await _taskRepo.CreateAsync("pending", UserId, Ct);
        await _taskRepo.CreateAsync("running", UserId, Ct);
        _taskRepo.Tasks["running"].Status = ProcessingStatus.InProgress;
        await _taskRepo.CreateAsync("done", UserId, Ct);
        _taskRepo.Tasks["done"].Status = ProcessingStatus.Done;

        Assert.Equal(2, await ImportService().RequeueUnfinishedAsync(Ct));
    }

    // --- polling ---------------------------------------------------------------------------

    [Fact]
    public async Task Poll_StoresTheNewScrobbles_MovesTheCursor_AndPublishes()
    {
        await LinkedAsync(T0);
        Scrobbles(-5, 1, 2); // -5 was before the cursor: not read again

        Assert.Equal(1, await PollService().PollDueAsync(Ct));

        Assert.Equal(2, _plays.Events.Count);
        Assert.Equal(T0.AddMinutes(2), Account.LastScrobbleAt);
        Assert.Equal(T0.AddMinutes(2), Account.LastListeningAt);
        Assert.Equal(2, Assert.IsType<ListeningImported>(Assert.Single(_publisher.Published)).NewPlays);
        Assert.Equal(T0, _lastFm.Calls[0].From);
    }

    [Fact]
    public async Task Poll_NothingNew_DoesNotPublish_AndWaitsTheIdleInterval()
    {
        await LinkedAsync(T0);

        await PollService().PollDueAsync(Ct);

        Assert.Empty(_publisher.Published);
        Assert.InRange(Account.NextPollAt - DateTime.UtcNow, TimeSpan.FromSeconds(890), TimeSpan.FromSeconds(900));
    }

    [Fact]
    public async Task Poll_NowPlaying_IsKept_AndTheNextPollIsSoon()
    {
        await LinkedAsync(T0);
        _lastFm.NowPlaying = new LastFmNowPlaying("Radiohead", "Reckoner", null, null);

        await PollService().PollDueAsync(Ct);

        Assert.Equal("Reckoner", _nowPlaying.Get(UserId)!.Track);
        Assert.InRange(Account.NextPollAt - DateTime.UtcNow, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Poll_TrackChanges_ArePublished_OnlyWhenTheyChange()
    {
        await LinkedAsync(T0);
        var poll = PollService();
        void Due() => Account.NextPollAt = DateTime.UtcNow;

        _lastFm.NowPlaying = new LastFmNowPlaying("Radiohead", "Reckoner", null, "mb-radiohead");
        await poll.PollDueAsync(Ct);
        Due();
        await poll.PollDueAsync(Ct); // same track: nothing new
        _lastFm.NowPlaying = null;
        Due();
        await poll.PollDueAsync(Ct); // stopped

        var events = NowPlayingEvents.ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal((UserId, "Reckoner", "mb-radiohead"), (events[0].UserId, events[0].Track, events[0].ArtistMbid));
        Assert.Null(events[1].Track);
    }

    [Fact]
    public async Task UserActive_PollsSoon_ThenKeepsTheFastCadence()
    {
        await LinkedAsync(T0);
        Account.NextPollAt = DateTime.UtcNow.AddMinutes(10);

        await AccountService().MarkActiveAsync(UserId, DateTime.UtcNow, Ct);

        Assert.True(Account.NextPollAt <= DateTime.UtcNow);
        await PollService().PollDueAsync(Ct); // nothing playing, but the page is open
        Assert.InRange(Account.NextPollAt - DateTime.UtcNow, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task UserActive_OfAUserWithoutLastFm_DoesNothing()
    {
        await AccountService().MarkActiveAsync("anon_x", DateTime.UtcNow, Ct);

        Assert.False(_activity.IsActive("anon_x", DateTime.UtcNow));
    }

    [Fact]
    public async Task Poll_Backlog_ReadsTheOldestPagesFirst_AndContinuesNextTime()
    {
        await LinkedAsync(T0);
        Scrobbles(1, 2, 3, 4, 5, 6, 7); // 4 pages of 2, MaxPollPages = 2

        await PollService().PollDueAsync(Ct);

        // pages 4 and 3 (oldest): minutes 1, 2, 3
        Assert.Equal(3, _plays.Events.Count);
        Assert.Equal(T0.AddMinutes(3), Account.LastScrobbleAt);

        Account.NextPollAt = DateTime.UtcNow;
        await PollService().PollDueAsync(Ct);

        Assert.Equal(7, _plays.Events.Count);
        Assert.Equal(T0.AddMinutes(7), Account.LastScrobbleAt);
    }

    [Fact]
    public async Task Poll_SessionRevoked_SuspendsTheAccount()
    {
        await LinkedAsync(T0);
        _lastFm.Failures.Enqueue(new LastFmApiException(LastFmApiException.InvalidSessionKey, "Invalid session key"));

        await PollService().PollDueAsync(Ct);

        Assert.Equal((LastFmAccountStatus.Suspended, ErrorCodes.LastFmAuthFailed), (Account.Status, Account.ErrorCode));
        Assert.Equal(0, await PollService().PollDueAsync(Ct)); // not read any more
    }

    [Fact]
    public async Task Poll_LastFmDown_RetriesLater_WithoutSuspending()
    {
        await LinkedAsync(T0);
        _lastFm.Failures.Enqueue(new HttpRequestException("down"));

        await PollService().PollDueAsync(Ct);

        Assert.Equal(LastFmAccountStatus.Active, Account.Status);
        Assert.True(Account.NextPollAt > DateTime.UtcNow);
        Assert.Equal(T0, Account.LastScrobbleAt);
    }

    // --- user data -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteUserData_StopsTheImports_AndDeletesAccountAndImports()
    {
        var importId = await AccountService().StartImportAsync((await LinkedAsync(T0)).UserId, Ct);
        _nowPlaying.Set(UserId, new LastFmNowPlaying("A", "B", null, null), T0);

        var deletion = await UserData().DeleteAsync(UserId, Ct);

        Assert.Equal(1, deletion.Deleted["lastfmAccounts"]);
        Assert.Equal(1, deletion.Deleted["lastfmImports"]);
        Assert.Equal(1, deletion.Deleted["lastfmImportsStopped"]);
        Assert.Empty(_accountRepo.Accounts);
        Assert.False(_taskRepo.Tasks.ContainsKey(importId));
        Assert.Null(_nowPlaying.Get(UserId));
    }

    [Fact]
    public async Task DeleteEverything_StopsEveryImport_AndDeletesEveryAccount()
    {
        await LinkedAsync(T0);
        await _accounts.LinkAsync("u2", new LastFmSession("bob", "sk2"), T0, Ct);
        var importId = await AccountService().StartImportAsync(UserId, Ct);
        _nowPlaying.Set("u2", new LastFmNowPlaying("A", "B", null, null), T0);

        var deletion = await UserData().DeleteEverythingAsync(Ct);

        Assert.Equal((2L, 1L, 1L), (deletion.Deleted["lastfmAccounts"], deletion.Deleted["lastfmImports"], deletion.Deleted["lastfmImportsStopped"]));
        Assert.Empty(_accountRepo.Accounts);
        Assert.Empty(_taskRepo.Tasks);
        Assert.Null(_nowPlaying.Get("u2"));
        // the queued import no longer runs
        await ImportService().RunAsync(await DequeueAsync(), Ct);
        Assert.Empty(_lastFm.Calls);
        Assert.False(_taskRepo.Tasks.ContainsKey(importId));
    }

    // --- rules -----------------------------------------------------------------------------

    [Fact]
    public async Task NoScrobbleForDays_IsAHint_OnlyForActiveAccounts()
    {
        var account = await LinkedAsync(T0);
        var now = T0.AddDays(4);

        Assert.Equal(ErrorCodes.LastFmSpotifyNotScrobbling, _hints.WarningOf(account, now));
        account.LastScrobbleAt = T0.AddDays(2);
        Assert.Null(_hints.WarningOf(account, now)); // a scrobble 2 days ago
        account.LastScrobbleAt = T0;
        account.Status = LastFmAccountStatus.Suspended;
        Assert.Null(_hints.WarningOf(account, now)); // a suspended account already has its error
        Assert.Null(_hints.WarningOf(await LinkedAsync(now.AddDays(-1)), now)); // linked yesterday
    }

    [Fact]
    public async Task Account_CarriesTheHint()
    {
        await LinkedAsync(DateTime.UtcNow.AddDays(-10));

        Assert.Equal(ErrorCodes.LastFmSpotifyNotScrobbling, (await AccountService().GetAsync(UserId, Ct)).Warning);
    }

    [Fact]
    public void Cadence_DependsOnWhatTheUserIsDoing()
    {
        Assert.Equal(T0.AddSeconds(15), _cadence.NextPollAt(T0, fast: true, lastListeningAt: null));
        Assert.Equal(T0.AddSeconds(120), _cadence.NextPollAt(T0, fast: false, lastListeningAt: T0.AddMinutes(-59)));
        Assert.Equal(T0.AddSeconds(900), _cadence.NextPollAt(T0, fast: false, lastListeningAt: T0.AddMinutes(-61)));
        Assert.Equal(T0.AddSeconds(900), _cadence.NextPollAt(T0, fast: false, lastListeningAt: null));
    }

    [Theory]
    [InlineData(LastFmApiException.RateLimitExceeded, ErrorCodes.LastFmRateLimited, false)]
    [InlineData(LastFmApiException.InvalidSessionKey, ErrorCodes.LastFmAuthFailed, true)]
    [InlineData(LastFmApiException.AuthenticationFailed, ErrorCodes.LastFmAuthFailed, true)]
    [InlineData(LastFmApiException.InvalidParameters, ErrorCodes.LastFmAuthFailed, true)]
    [InlineData(LastFmApiException.LoginRequired, ErrorCodes.LastFmUserPrivate, true)]
    [InlineData(LastFmApiException.ServiceOffline, ErrorCodes.LastFmUnavailable, false)]
    [InlineData(LastFmApiException.SuspendedApiKey, ErrorCodes.LastFmUnavailable, false)]
    public void ErrorCodes_OfLastFm(int lastFmCode, string expected, bool accountProblem)
    {
        var code = _errors.ToErrorCode(new LastFmApiException(lastFmCode, "x"), ErrorCodes.LastFmImportFailed);

        Assert.Equal(expected, code);
        Assert.Equal(accountProblem, _errors.IsAccountProblem(code));
    }

    [Fact]
    public void ErrorCodes_OfTheNetwork_AndUnexpected()
    {
        Assert.Equal(ErrorCodes.LastFmUnavailable, _errors.ToErrorCode(new HttpRequestException("x"), ErrorCodes.LastFmImportFailed));
        Assert.Equal(ErrorCodes.LastFmUnavailable, _errors.ToErrorCode(new TaskCanceledException(), ErrorCodes.LastFmImportFailed));
        Assert.Equal(ErrorCodes.LastFmImportFailed, _errors.ToErrorCode(new InvalidOperationException(), ErrorCodes.LastFmImportFailed));
        Assert.True(_errors.IsConfigurationProblem(new LastFmApiException(LastFmApiException.InvalidApiKey, "x")));
    }

    [Fact]
    public void ListeningEvent_KeyIsArtistAndTrackInLowerCase()
    {
        var e = Assert.Single(new LastFmListeningMapper().ToListeningEvents(
            [new LastFmScrobble(" Radiohead ", "Reckoner", "In Rainbows", "a74b1b7f", "t1", T0)], UserId, "ref"));

        Assert.Equal("lastfm:radiohead|reckoner", e.TrackUri);
        Assert.Equal(ListeningEvent.BuildId(UserId, "lastfm:radiohead|reckoner", T0), e.Id);
        Assert.Equal((T0, T0), (e.PlayedAt, e.StartedAt));
        Assert.Equal(("a74b1b7f", "t1"), (e.ArtistMbid, e.TrackMbid));
        Assert.Null(e.MsPlayed);
        Assert.Equal([ListeningSources.LastFm], e.Sources);
    }
}
