using CityFy.Retrieve.Spotify.SubServices;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Services;

/// <summary>
/// Deletes what this service keeps about a user: running/queued retrieves are stopped, then the
/// jobs and the linked account (refresh token included) are removed. The scheduled retrieve
/// skips the user from then on (no account). Plays are deleted by Catalog.
/// </summary>
public sealed class UserDataService(
    IRetrieveJobSubService jobs,
    ISpotifyAccountStoreSubService accounts,
    ISpotifyAccessTokenSubService accessTokens,
    ILogger<UserDataService> logger) : IUserDataService
{
    public const string ServiceName = "RetrieveSpotify";

    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var stopped = await jobs.CancelAllActiveAsync(userId, ct);
        var deletedJobs = await jobs.DeleteAllAsync(userId, ct);
        var deletedAccounts = await accounts.DeleteAsync(userId, ct);
        accessTokens.Forget(userId);

        logger.LogInformation("Data of user {UserId} deleted: {Jobs} retrieve jobs ({Stopped} stopped), {Accounts} Spotify account",
            userId, deletedJobs, stopped, deletedAccounts);
        return new UserDataDeletion(ServiceName, new Dictionary<string, long>
        {
            ["spotifyAccounts"] = deletedAccounts,
            ["retrieveJobs"] = deletedJobs,
            ["retrieveJobsStopped"] = stopped,
        });
    }
}
