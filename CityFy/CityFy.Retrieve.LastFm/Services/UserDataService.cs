using CityFy.Retrieve.LastFm.SubServices;
using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Services;

/// <summary>
/// Deletes what this service keeps about a user: running imports are stopped first (they must not
/// store plays afterwards), then the imports and the account (session key included). The plays are
/// deleted by Catalog, after this service (the gateway calls it first).
/// </summary>
public sealed class UserDataService(
    ILastFmImportJobSubService imports,
    ILastFmAccountStoreSubService accounts,
    INowPlayingStoreSubService nowPlaying,
    IUserActivitySubService activity,
    ILogger<UserDataService> logger) : IUserDataService
{
    public const string ServiceName = "RetrieveLastFm";

    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var stopped = await imports.CancelAllActiveAsync(userId, ct);
        var deletedImports = await imports.DeleteAllAsync(userId, ct);
        var deletedAccounts = await accounts.DeleteAsync(userId, ct);
        nowPlaying.Forget(userId);
        activity.Forget(userId);

        logger.LogInformation("Data of user {UserId} deleted: {Imports} Last.fm imports ({Stopped} stopped), {Accounts} Last.fm account",
            userId, deletedImports, stopped, deletedAccounts);
        return new UserDataDeletion(ServiceName, new Dictionary<string, long>
        {
            ["lastfmAccounts"] = deletedAccounts,
            ["lastfmImports"] = deletedImports,
            ["lastfmImportsStopped"] = stopped,
        });
    }

    public async Task<UserDataDeletion> DeleteEverythingAsync(CancellationToken ct)
    {
        // Imports first: nothing may write plays or accounts afterwards
        var stopped = await imports.CancelEverythingAsync(ct);
        var deletedImports = await imports.DeleteEverythingAsync(ct);
        var deletedAccounts = await accounts.DeleteAllAsync(ct);
        nowPlaying.ForgetAll();
        activity.ForgetAll();

        logger.LogWarning("ALL Last.fm data deleted: {Accounts} accounts, {Imports} imports ({Stopped} stopped)",
            deletedAccounts, deletedImports, stopped);
        return new UserDataDeletion(ServiceName, new Dictionary<string, long>
        {
            ["lastfmAccounts"] = deletedAccounts,
            ["lastfmImports"] = deletedImports,
            ["lastfmImportsStopped"] = stopped,
        });
    }
}
