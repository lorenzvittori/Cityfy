namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IStreamingHistoryService
{
    /// <summary>
    /// Imports the extended streaming history files of an upload into 'listening_events'.
    /// Returns the number of new plays. Throws if the import cannot be completed.
    /// </summary>
    Task<int> ProcessExtractedFilesAsync(string uploadId, string userId, string extractDir, CancellationToken ct = default);
}
