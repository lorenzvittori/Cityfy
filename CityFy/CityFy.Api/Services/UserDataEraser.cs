using CityFy.Api.Models;
using CityFy.Api.SubServices;
using ServiceDefault.Errors;

namespace CityFy.Api.Services;

/// <summary>
/// Deletes everything CityFy keeps about a user, service by service (see
/// <see cref="IUserDataDeletionSubService"/>). When a service fails the whole deletion can simply be
/// repeated (USER_DATA_DELETION_INCOMPLETE).
/// </summary>
public sealed class UserDataEraser(IUserDataDeletionSubService deletion, ILogger<UserDataEraser> logger) : IUserDataEraser
{
    public async Task<UserDataDeletionResult> DeleteAsync(string userId, string requestedBy, CancellationToken ct)
    {
        var (deleted, failed) = await deletion.DeleteEverywhereAsync(userId, ct);

        if (failed.Count > 0)
            throw new CityFyException(ErrorCodes.UserDataDeletionIncomplete,
                $"Data of user '{userId}' not completely deleted: {string.Join(", ", failed)} did not answer. Retry to complete.",
                StatusCodes.Status502BadGateway);

        logger.LogInformation("Data of user {UserId} deleted (requested by {RequestedBy}): {Summary}", userId, requestedBy,
            string.Join("; ", deleted.Select(d => $"{d.Service} {string.Join(", ", d.Deleted.Select(kv => $"{kv.Key}={kv.Value}"))}")));
        return new UserDataDeletionResult(userId, deleted);
    }
}
