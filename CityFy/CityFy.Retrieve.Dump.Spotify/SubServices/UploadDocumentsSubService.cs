using CityFy.Retrieve.Dump.Spotify.Repositories;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class UploadDocumentsSubService(IStreamingRepository repo) : IUploadDocumentsSubService
{
    public const int MaxPageSize = 200;

    public async Task<IReadOnlyList<string>> GetAsync(string collectionName, int page, int pageSize, string? uploadId) =>
        (await repo.GetDocumentsPagedAsync(collectionName, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize), uploadId)).ToList();
}