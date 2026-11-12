using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

/// <summary>Uploaded archives and their extracted files on disk ('uploads/{id}').</summary>
public interface IUploadStorageSubService
{
    /// <summary>Stores the zip under a new upload id and extracts it. Throws UPLOAD_INVALID_ZIP for a broken archive.</summary>
    Task<UploadResult> SaveAndExtractAsync(IFormFile file, CancellationToken ct);

    /// <summary>The extracted upload, null when not found or not extracted yet.</summary>
    UploadResult? GetExtracted(string uploadId);

    /// <summary>Folder of the extracted files, null when missing.</summary>
    string? GetExtractedDirectory(string uploadId);

    /// <summary>
    /// Removes the upload folder. true = removed, false = still there (files in use), null = nothing to remove.
    /// </summary>
    Task<bool?> DeleteAsync(string uploadId, CancellationToken ct);
}