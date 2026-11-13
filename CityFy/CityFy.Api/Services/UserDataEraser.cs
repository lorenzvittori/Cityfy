using CityFy.Api.Auth;
using CityFy.Api.Models;
using CityFy.Api.SubServices;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Api.Services;

/// <summary>
/// Deletes everything CityFy keeps about a user, service by service (see
/// <see cref="IUserDataDeletionSubService"/>). When a service fails the whole deletion can simply be
/// repeated (USER_DATA_DELETION_INCOMPLETE).
/// </summary>
public sealed class UserDataEraser(
    IUserDataDeletionSubService deletion,
    IAnonymousSessionStoreSubService sessions,
    IUserStoreSubService users,
    ILogger<UserDataEraser> logger) : IUserDataEraser
{
    public async Task<UserDataDeletionResult> DeleteAsync(string userId, string requestedBy, CancellationToken ct)
    {
        var (deleted, failed) = await deletion.DeleteEverywhereAsync(userId, ct);

        if (failed.Count > 0)
            throw new CityFyException(ErrorCodes.UserDataDeletionIncomplete,
                $"Data of user '{userId}' not completely deleted: {string.Join(", ", failed)} did not answer. Retry to complete.",
                StatusCodes.Status502BadGateway);

        // An anonymous session without data is over: its cookie is refused from the next request
        // and so is an account: its cookie too is refused from the next request
        if (AuthSetup.SessionIdOf(userId) is { } sessionId)
            await sessions.MarkDeletedAsync(sessionId, DateTime.UtcNow, ct);
        else
            await users.DeleteAsync(userId, ct);

        logger.LogInformation("Data of user {UserId} deleted (requested by {RequestedBy}): {Summary}", userId, requestedBy,
            string.Join("; ", deleted.Select(Describe)));
        return new UserDataDeletionResult(userId, deleted);
    }

    private static string Describe(UserDataDeletion deletion) => deletion.Describe();
}
