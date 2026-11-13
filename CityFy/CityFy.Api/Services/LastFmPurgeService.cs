using CityFy.Api.Clients;
using CityFy.Api.Models;
using CityFy.Api.SubServices;
using ServiceDefault.Errors;

namespace CityFy.Api.Services;

public sealed class LastFmPurgeService(
    ILastFmServiceClient lastFm,
    IUserStoreSubService users,
    IUserDataDeletionSubService deletion,
    ILogger<LastFmPurgeService> logger) : ILastFmPurgeService
{
    public async Task<LastFmPurgeResult> PurgeAllAsync(string requestedBy, CancellationToken ct)
    {
        logger.LogWarning("Deletion of ALL Last.fm data requested by {RequestedBy}", requestedBy);

        // Nothing reads Last.fm or writes plays from it any more
        var lastFmService = await lastFm.DeleteEverythingAsync(ct);

        var deletedUsers = 0;
        var failedUsers = new List<string>();
        foreach (var userId in await users.ListIdsAsync(ct))
        {
            var (_, failed) = await deletion.DeleteEverywhereAsync(userId, ct);
            if (failed.Count > 0)
            {
                logger.LogError("Last.fm purge: data of {UserId} not completely deleted ({Services})", userId, string.Join(", ", failed));
                failedUsers.Add(userId);
                continue;
            }
            await users.DeleteAsync(userId, ct);
            deletedUsers++;
        }

        if (failedUsers.Count > 0)
            throw new CityFyException(ErrorCodes.UserDataDeletionIncomplete,
                $"{failedUsers.Count} accounts not completely deleted ({string.Join(", ", failedUsers.Take(10))}): repeat to complete",
                StatusCodes.Status502BadGateway);

        logger.LogWarning("ALL Last.fm data deleted: {Users} accounts, Retrieve.LastFm {Counts}", deletedUsers,
            lastFmService.Summary());
        return new LastFmPurgeResult(deletedUsers, lastFmService);
    }
}
