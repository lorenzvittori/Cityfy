using System.Security.Cryptography;
using CityFy.Retrieve.LastFm.Models;
using CityFy.Retrieve.LastFm.Options;
using CityFy.Retrieve.LastFm.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.LastFm.SubServices;

public sealed class LastFmAccountStoreSubService : ILastFmAccountStoreSubService
{
    private const string ProtectorPurpose = "CityFy.Retrieve.LastFm.SessionKey";

    private readonly ILastFmAccountRepository _repository;
    private readonly IDataProtector _protector;
    private readonly int _pollBatchSize;

    public LastFmAccountStoreSubService(ILastFmAccountRepository repository, IDataProtectionProvider dataProtection, IOptions<LastFmOptions> options)
    {
        _repository = repository;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _pollBatchSize = options.Value.PollBatchSize;
    }

    public async Task<LastFmAccount> LinkAsync(string userId, LastFmSession session, DateTime now, CancellationToken ct)
    {
        // The Last.fm account is the identity: already linked means its owner is logging in again
        if (await _repository.GetByUsernameAsync(session.Username, ct) is { } owner)
            userId = owner.UserId;

        var existing = await _repository.GetAsync(userId, ct);
        // Same Last.fm account linked again (e.g. after a revoke): resume from the old cursor.
        // Another Last.fm account for the same user: start over.
        var sameAccount = existing is not null && existing.Username == session.Username;
        var account = new LastFmAccount
        {
            UserId = userId,
            Username = session.Username,
            ProtectedSessionKey = _protector.Protect(session.SessionKey),
            Status = LastFmAccountStatus.Active,
            ErrorCode = null,
            LinkedAt = now,
            LastScrobbleAt = sameAccount ? existing!.LastScrobbleAt : now,
            LastListeningAt = sameAccount ? existing!.LastListeningAt : null,
            LastPolledAt = sameAccount ? existing!.LastPolledAt : null,
            NextPollAt = now,
            HistoryImport = sameAccount ? existing!.HistoryImport : null
        };
        await _repository.UpsertAsync(account, ct);
        return account;
    }

    public Task<LastFmAccount?> GetAsync(string userId, CancellationToken ct) => _repository.GetAsync(userId, ct);

    public async Task<LastFmAccount> GetRequiredAsync(string userId, CancellationToken ct) =>
        await _repository.GetAsync(userId, ct)
        ?? throw new CityFyException(ErrorCodes.LastFmNotLinked, $"No Last.fm account linked to user '{userId}'", StatusCodes.Status404NotFound);

    public Task<IReadOnlyList<LastFmAccount>> ListAsync(CancellationToken ct) => _repository.ListAsync(ct);

    public Task<IReadOnlyList<LastFmAccount>> ListDueAsync(DateTime now, CancellationToken ct) =>
        _repository.ListDueAsync(now, _pollBatchSize, ct);

    public string GetSessionKey(LastFmAccount account)
    {
        try
        {
            return _protector.Unprotect(account.ProtectedSessionKey);
        }
        catch (CryptographicException ex)
        {
            throw new CityFyException(ErrorCodes.LastFmAuthFailed,
                $"Session key of '{account.Username}' can no longer be decrypted ({ex.Message}): link the account again",
                StatusCodes.Status401Unauthorized);
        }
    }

    public Task UpdatePollAsync(string userId, DateTime polledAt, DateTime lastScrobbleAt, DateTime? lastListeningAt, DateTime nextPollAt, CancellationToken ct) =>
        _repository.UpdatePollAsync(userId, polledAt, lastScrobbleAt, lastListeningAt, nextPollAt, ct);

    public Task SuspendAsync(string userId, string errorCode, CancellationToken ct) => _repository.SuspendAsync(userId, errorCode, ct);

    public Task SetNextPollAsync(string userId, DateTime nextPollAt, CancellationToken ct) => _repository.SetNextPollAsync(userId, nextPollAt, ct);

    public Task PullNextPollAsync(string userId, DateTime at, CancellationToken ct) => _repository.PullNextPollAsync(userId, at, ct);

    public Task SetHistoryImportAsync(string userId, LastFmHistoryImport? import, CancellationToken ct) =>
        _repository.SetHistoryImportAsync(userId, import, ct);

    public Task<long> DeleteAsync(string userId, CancellationToken ct) => _repository.DeleteAsync(userId, ct);

    public Task<long> DeleteAllAsync(CancellationToken ct) => _repository.DeleteAllAsync(ct);
}
