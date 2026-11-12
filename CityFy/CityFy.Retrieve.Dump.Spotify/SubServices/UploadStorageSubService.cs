using System.IO.Compression;
using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.Models;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class UploadStorageSubService(IFileClient files, ILogger<UploadStorageSubService> logger) : IUploadStorageSubService
{
    private const string UploadsFolder = "uploads";
    private const string ExtractedFolder = "extracted";

    // A stopped import may still hold a file for a moment: a few attempts
    private const int DeleteAttempts = 5;
    private const int DeleteRetryDelayMs = 400;

    public async Task<UploadResult> SaveAndExtractAsync(IFormFile file, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var uploadDir = files.GetFullPath(UploadsFolder, id);
        files.CreateDirectory(uploadDir);

        var zipPath = Path.Combine(uploadDir, Path.GetFileName(file.FileName));
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            ms.Seek(0, SeekOrigin.Begin);
            await files.SaveFileAsync(zipPath, ms);
        }

        var extractDir = Path.Combine(uploadDir, ExtractedFolder);
        files.CreateDirectory(extractDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
        }
        catch (InvalidDataException ex)
        {
            throw new CityFyException(ErrorCodes.UploadInvalidZip, "The file is not a valid zip archive", inner: ex);
        }

        return new UploadResult { Id = id, UploadPath = uploadDir, ExtractedPath = extractDir, Files = files.ListFiles(extractDir) };
    }

    public UploadResult? GetExtracted(string uploadId)
    {
        if (GetExtractedDirectory(uploadId) is not { } extractDir) return null;

        return new UploadResult
        {
            Id = uploadId,
            UploadPath = files.GetFullPath(UploadsFolder, uploadId),
            ExtractedPath = extractDir,
            Files = files.ListFiles(extractDir)
        };
    }

    public string? GetExtractedDirectory(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId)) return null;
        var extractDir = files.GetFullPath(UploadsFolder, uploadId, ExtractedFolder);
        return Directory.Exists(extractDir) ? extractDir : null;
    }

    public async Task<bool?> DeleteAsync(string uploadId, CancellationToken ct)
    {
        var path = files.GetFullPath(UploadsFolder, uploadId);
        if (!Directory.Exists(path)) return null;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (IOException) when (attempt < DeleteAttempts)
            {
                await Task.Delay(DeleteRetryDelayMs * attempt, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Upload folder {Path} could not be removed", path);
                return false;
            }
        }
    }
}