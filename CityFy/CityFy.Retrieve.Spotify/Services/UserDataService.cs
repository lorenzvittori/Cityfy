using CityFy.Retrieve.Spotify.Repositories;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Services;

/// <summary>
/// Deletes what this service keeps about a user: running/queued retrieves are stopped, then the
/// jobs and the linked account (refresh token included) are removed. The scheduled retrieve
/// skips the user from then on (no account). Plays are deleted by Elaboration.
/// </summary>
public sealed class UserDataService(
    ISpotifyRetrieveService retrieve,
    ISpotifyRepository jobs,
    ISpotifyAccountRepository accounts,
    ISpotifyAccountService accountService,
    ILogger<UserDataService> logger) : IUserDataService
{
    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var cancelled = 0;
        foreach (var job in await jobs.ListActiveTasksAsync(userId, ct))
        {
            try
            {
                await retrieve.CancelAsync(job.UploadId, ct);
                cancelled++;
            }
            catch (CityFyException e) when (e.Code is ErrorCodes.ProcessNotCancellable or ErrorCodes.RetrieveJobNotFound)
            {
                // finished meanwhile: nothing to stop
            }
        }

        var deletedJobs = await jobs.DeleteTasksAsync(userId, ct);
        var deletedAccounts = await accounts.DeleteAsync(userId, ct);
        accountService.Forget(userId);

        logger.LogInformation("Data of user {UserId} deleted: {Jobs} retrieve jobs ({Cancelled} stopped), {Accounts} Spotify account",
            userId, deletedJobs, cancelled, deletedAccounts);
        return new UserDataDeletion("RetrieveSpotify", new Dictionary<string, long>
        {
            ["spotifyAccounts"] = deletedAccounts,
            ["retrieveJobs"] = deletedJobs,
            ["retrieveJobsStopped"] = cancelled,
        });
    }
}
