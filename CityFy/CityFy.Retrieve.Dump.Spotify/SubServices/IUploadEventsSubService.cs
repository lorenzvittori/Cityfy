namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public interface IUploadEventsSubService
{
    /// <summary>UploadReceived: the upload is extracted and waits for the import (consumed by this service).</summary>
    Task PublishReceivedAsync(string uploadId, string userId, CancellationToken ct);

    /// <summary>ListeningImported: new plays for the user (the orchestrator rebuilds the city).</summary>
    Task PublishImportedAsync(string uploadId, string userId, int newPlays, CancellationToken ct);
}