using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IZipUploadService
{
    /// <summary>
    /// Validates, stores and extracts the zip, creates the Pending task and publishes UploadReceived.
    /// Throws UPLOAD_* CityFyExceptions for invalid input.
    /// </summary>
    Task<UploadResult> HandleZipUploadAsync(IFormFile file, string userId, CancellationToken ct);

    UploadResult? GetUploadResult(string id);
}