using CityFy.Retrieve.Dump.Spotify.Repositories;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class UploadTaskService(IStreamingRepository repo) : IUploadTaskService
{
    public const int MaxPageSize = 200;

    public Task<ProcessingTask?> GetAsync(string uploadId, CancellationToken ct) => repo.GetTaskByUploadIdAsync(uploadId);

    public async Task<IReadOnlyList<ProcessingTask>> ListAsync(int page, int pageSize, CancellationToken ct) =>
        (await repo.GetTasksPagedAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize))).ToList();

    public async Task<IReadOnlyList<string>> GetDocumentsAsync(string collectionName, int page, int pageSize, string? uploadId, CancellationToken ct) =>
        (await repo.GetDocumentsPagedAsync(collectionName, Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize), uploadId)).ToList();
}
