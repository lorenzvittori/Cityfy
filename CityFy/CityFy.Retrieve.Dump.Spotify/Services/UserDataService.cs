using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Errors;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

/// <summary>
/// Deletes what this service keeps about a user: running/queued imports are stopped, then the
/// uploaded archives with their extracted files, the legacy per-upload collections and the upload
/// tasks are removed. Plays are deleted by Elaboration.
/// </summary>
public sealed class UserDataService(
    IStreamingRepository repo,
    IUploadProcessor processor,
    IFileClient files,
    ILogger<UserDataService> logger) : IUserDataService
{
    public async Task<UserDataDeletion> DeleteAsync(string userId, CancellationToken ct)
    {
        var uploads = await repo.GetTasksByUserAsync(userId, ct);
        var stopped = 0;
        var folders = 0;
        var foldersLeft = 0;

        foreach (var upload in uploads)
        {
            if (upload.Status is ProcessingStatus.Pending or ProcessingStatus.InProgress)
            {
                try
                {
                    await processor.CancelAsync(upload.UploadId);
                    stopped++;
                }
                catch (CityFyException e) when (e.Code is ErrorCodes.ProcessNotCancellable or ErrorCodes.UploadNotFound)
                {
                    // finished meanwhile
                }
            }

            await repo.DropLegacyUploadAsync(upload.UploadId, ct);
            switch (await DeleteFolderAsync(files.GetFullPath("uploads", upload.UploadId), ct))
            {
                case true: folders++; break;
                case false: foldersLeft++; break;
            }
        }

        var tasks = await repo.DeleteTasksByUserAsync(userId, ct);
        logger.LogInformation("Data of user {UserId} deleted: {Tasks} uploads ({Stopped} stopped), {Folders} folders ({Left} could not be removed)",
            userId, tasks, stopped, folders, foldersLeft);

        return new UserDataDeletion("RetrieveDumpSpotify", new Dictionary<string, long>
        {
            ["uploads"] = tasks,
            ["uploadsStopped"] = stopped,
            ["uploadFolders"] = folders,
            ["uploadFoldersNotRemoved"] = foldersLeft,
        });
    }

    /// <summary>
    /// true = removed, false = still there (files in use), null = nothing to remove. A stopped import
    /// may still hold a file for a moment: a few attempts.
    /// </summary>
    private async Task<bool?> DeleteFolderAsync(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path)) return null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(400 * attempt, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Upload folder {Path} could not be removed", path);
                return false;
            }
        }
    }
}
