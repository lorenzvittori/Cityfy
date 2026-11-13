using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.SubServices;

namespace CityFy.Api.Services;

public sealed class AccountService(
    ILastFmServiceClient lastFm,
    IUserStoreSubService users,
    IAnonymousSessionStoreSubService sessions,
    IUserDataTransferSubService transfer,
    ILogger<AccountService> logger) : IAccountService
{
    public Task<string> GetLastFmLoginUrlAsync(CancellationToken ct) => lastFm.GetAuthorizeUrlAsync(ct);

    public async Task<AccountLoginResult> LoginWithLastFmAsync(string token, string? anonymousSessionId, CancellationToken ct)
    {
        var linked = await lastFm.LinkAsync(users.NewUserId(), token, ct);
        var isNew = await users.RecordLoginAsync(linked.UserId, linked.Username, DateTime.UtcNow, ct);
        var moved = anonymousSessionId is { } sessionId ? await TransferSessionAsync(sessionId, linked.UserId, ct) : null;

        logger.LogInformation("Last.fm login of {Username} as {UserId} ({Kind}, data of an anonymous session: {Transfer})",
            linked.Username, linked.UserId, isNew ? "new account" : "existing account",
            moved is null ? "none" : moved.Complete ? "moved" : $"to retry: {string.Join(", ", moved.Failed)}");
        return new AccountLoginResult(linked.UserId, linked.Username, isNew, moved);
    }

    public async Task<bool> ExistsAsync(string userId, CancellationToken ct) => await users.GetAsync(userId, ct) is not null;

    public async Task<int> RetryTransfersAsync(CancellationToken ct)
    {
        var completed = 0;
        foreach (var session in await sessions.ListTransferringAsync(ct))
            if (await TransferSessionAsync(session.Id, session.TransferTo!, ct) is { Complete: true })
                completed++;
        return completed;
    }

    /// <summary>Null when the session can no longer be moved (expired, deleted, or moved to another account).</summary>
    private async Task<SessionTransferResult?> TransferSessionAsync(string sessionId, string toUserId, CancellationToken ct)
    {
        // Transferring first: from now on the cleanup of expired sessions leaves it alone
        if (!await sessions.MarkTransferringAsync(sessionId, toUserId, DateTime.UtcNow, ct))
            return null;

        var failed = await transfer.TransferEverywhereAsync(AuthSetup.AnonymousUserId(sessionId), toUserId, ct);
        if (failed.Count == 0)
            await sessions.MarkTransferredAsync(sessionId, DateTime.UtcNow, ct);
        else
            logger.LogWarning("Data of anonymous session {SessionId} not completely moved to {UserId}: {Services}, retried later",
                sessionId, toUserId, string.Join(", ", failed));
        return new SessionTransferResult(failed.Count == 0, failed);
    }
}
