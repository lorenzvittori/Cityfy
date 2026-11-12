namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public interface IStreamingHistoryImportSubService
{
    /// <summary>
    /// Imports the extended streaming history files of an upload into 'listening_events', in
    /// batches, reporting progress on the task. Idempotent (upsert). Returns the number of new plays.
    /// Throws UPLOAD_NO_STREAMING_FILES / UPLOAD_UNREADABLE_FILES when there is nothing to import.
    /// </summary>
    Task<int> ImportAsync(string uploadId, string userId, string extractDir, CancellationToken ct);
}