using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.SubServices;

namespace CityFy.Retrieve.LastFm.Services;

public sealed class LastFmAccountService(
    ILastFmAuthSubService auth,
    ILastFmAccountStoreSubService accounts,
    ILastFmImportJobSubService imports,
    INowPlayingStoreSubService nowPlaying,
    IUserActivitySubService activity,
    ILastFmAccountHintSubService hints,
    ILogger<LastFmAccountService> logger) : ILastFmAccountService
{
    public string GetAuthorizeUrl() => auth.GetAuthorizeUrl();

    public async Task<LinkResponse> LinkAsync(string userId, string token, CancellationToken ct)
    {
        var session = await auth.ExchangeTokenAsync(token, ct);
        var account = await accounts.LinkAsync(userId, session, DateTime.UtcNow, ct);

        // Linked again after a revoke: the history is already in, the polling resumes from its cursor
        var importId = account.HistoryImport is null ? await QueueImportAsync(account, ct) : null;

        logger.LogInformation("Last.fm account {Username} linked to user {UserId} (history import {ImportId})",
            account.Username, account.UserId, importId ?? "not needed");
        return new LinkResponse(account.UserId, account.Username, importId);
    }

    public async Task<LastFmAccountResponse> GetAsync(string userId, CancellationToken ct)
    {
        var account = await accounts.GetRequiredAsync(userId, ct);
        return LastFmAccountResponse.From(account, nowPlaying.Get(userId), hints.WarningOf(account, DateTime.UtcNow));
    }

    public async Task<IReadOnlyList<LastFmAccountResponse>> ListAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return (await accounts.ListAsync(ct)).Select(a => LastFmAccountResponse.From(a, nowPlaying.Get(a.UserId), hints.WarningOf(a, now))).ToList();
    }

    public async Task<string> StartImportAsync(string userId, CancellationToken ct)
    {
        var account = await accounts.GetRequiredAsync(userId, ct);
        var stopped = await imports.CancelAllActiveAsync(userId, ct);
        var importId = await QueueImportAsync(account, ct);
        logger.LogInformation("Last.fm history import {ImportId} queued for user {UserId} ({Stopped} previous stopped)", importId, userId, stopped);
        return importId;
    }

    public async Task PollNowAsync(string userId, CancellationToken ct)
    {
        await accounts.GetRequiredAsync(userId, ct);
        await accounts.SetNextPollAsync(userId, DateTime.UtcNow, ct);
    }

    public async Task MarkActiveAsync(string userId, DateTime at, CancellationToken ct)
    {
        if (await accounts.GetAsync(userId, ct) is null)
            return;
        activity.MarkActive(userId, at);
        // the bar must show the track soon: poll within the fast interval, never later than planned
        await accounts.PullNextPollAsync(userId, DateTime.UtcNow, ct);
    }

    // The account records the import (resume point) before it is queued: the job always finds it.
    // Upper bound = polling cursor: what comes after it is read by the polling.
    private async Task<string> QueueImportAsync(LastFmAccount account, CancellationToken ct)
    {
        var importId = imports.NewImportId();
        await accounts.SetHistoryImportAsync(account.UserId,
            new LastFmHistoryImport(importId, account.LastScrobbleAt, NextPage: 1, TotalPages: null, Inserted: 0), ct);
        await imports.CreateAndEnqueueAsync(importId, account.UserId, ct);
        return importId;
    }
}
