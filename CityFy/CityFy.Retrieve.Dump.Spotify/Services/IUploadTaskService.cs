using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

/// <summary>Read side of the uploads: task status and raw documents (admin panel, debug).</summary>
public interface IUploadTaskService
{
    Task<ProcessingTask?> GetAsync(string uploadId, CancellationToken ct);

    /// <summary>Uploads of every user, most recent first.</summary>
    Task<IReadOnlyList<ProcessingTask>> ListAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>Raw JSON documents of a collection, optionally filtered by upload.</summary>
    Task<IReadOnlyList<string>> GetDocumentsAsync(string collectionName, int page, int pageSize, string? uploadId, CancellationToken ct);
}
