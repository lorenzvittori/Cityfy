using CityFy.Retrieve.Dump.Spotify.SubServices;
using ServiceDefault.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services;

public sealed class UploadTaskService(IUploadTaskSubService tasks, IUploadDocumentsSubService documents) : IUploadTaskService
{
    public Task<ProcessingTask?> GetAsync(string uploadId, CancellationToken ct) => tasks.GetAsync(uploadId);

    public Task<IReadOnlyList<ProcessingTask>> ListAsync(int page, int pageSize, CancellationToken ct) => tasks.ListAsync(page, pageSize);

    public Task<IReadOnlyList<string>> GetDocumentsAsync(string collectionName, int page, int pageSize, string? uploadId, CancellationToken ct) =>
        documents.GetAsync(collectionName, page, pageSize, uploadId);
}