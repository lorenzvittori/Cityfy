namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public interface IUploadDocumentsSubService
{
    /// <summary>Raw JSON documents of a collection (debug), paged, optionally filtered by upload.</summary>
    Task<IReadOnlyList<string>> GetAsync(string collectionName, int page, int pageSize, string? uploadId);
}