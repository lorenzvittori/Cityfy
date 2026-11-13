using System.Text.Json;

namespace CityFy.Api.Clients;

/// <summary>
/// Retrieve.Dump.Spotify: uploads of the Spotify export. Registered without the standard resilience
/// handler: uploads are large and must not be retried or time out at 30 s.
/// </summary>
public interface IDumpClient : IUserDataOwner, IUserDataHolder
{
    Task<UploadAccepted> UploadAsync(Stream zip, string fileName, string userId, CancellationToken ct);

    /// <summary>Throws UPLOAD_NOT_FOUND (404).</summary>
    Task<TaskInfo> GetUploadAsync(string uploadId, CancellationToken ct);

    Task<JsonElement> CancelUploadAsync(string uploadId, CancellationToken ct);

    /// <summary>Admin: uploads of every user (paged, most recent first), with progress (passed through as is).</summary>
    Task<JsonElement> ListUploadsAsync(int page, int pageSize, CancellationToken ct);
}
