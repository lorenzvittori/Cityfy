using CityFy.Retrieve.Dump.Spotify.SubServices;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

/// <summary>
/// Deletes what this service keeps about a user: running/queued imports are stopped, then the
/// uploaded archives with their extracted files, the legacy per-upload collections and the upload
/// tasks are removed. Plays are deleted by Elaboration.
/// </summary>
public sealed class UserDataService(
    IUploadTaskSubService tasks,
    IUploadStorageSubService storage,
    ILogger<UserDataService> logger) : IUserDataService
{
    public const string ServiceName = "RetrieveDumpSpotify";

    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var stopped = 0;
        var folders = 0;
        var foldersLeft = 0;

        foreach (var upload in await tasks.ListByUserAsync(userId, ct))
        {
            if (await tasks.CancelIfActiveAsync(upload))
                stopped++;

            await tasks.DropLegacyAsync(upload.UploadId, ct);
            switch (await storage.DeleteAsync(upload.UploadId, ct))
            {
                case true: folders++; break;
                case false: foldersLeft++; break;
            }
        }

        var deleted = await tasks.DeleteAllAsync(userId, ct);
        logger.LogInformation("Data of user {UserId} deleted: {Tasks} uploads ({Stopped} stopped), {Folders} folders ({Left} could not be removed)",
            userId, deleted, stopped, folders, foldersLeft);

        return new UserDataDeletion(ServiceName, new Dictionary<string, long>
        {
            ["uploads"] = deleted,
            ["uploadsStopped"] = stopped,
            ["uploadFolders"] = folders,
            ["uploadFoldersNotRemoved"] = foldersLeft,
        });
    }
}