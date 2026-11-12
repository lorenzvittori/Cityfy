namespace CityFy.Retrieve.Dump.Spotify.Services;

public interface IUploadProcessor
{
    /// <summary>
    /// Imports an extracted upload and publishes ListeningImported. Throws on failure: the
    /// messaging layer retries, and marks the task Failed after the last attempt.
    /// A cancellation by the admin ends normally with the task Cancelled (no retry).
    /// </summary>
    Task ProcessAsync(string uploadId, CancellationToken ct);

    /// <summary>Failed with the error code of the exception (UPLOAD_PROCESSING_FAILED if not a CityFyException).</summary>
    Task MarkFailedAsync(string uploadId, Exception error);

    /// <summary>
    /// Admin: Pending -> Cancelled (never starts); InProgress -> Cancelled and the running import stops.
    /// Throws UPLOAD_NOT_FOUND (404) or PROCESS_NOT_CANCELLABLE (409) when already finished.
    /// </summary>
    Task CancelAsync(string uploadId);
}
