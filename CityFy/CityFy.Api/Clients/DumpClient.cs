using ServiceDefault.Clients;
using System.Text.Json;
using ServiceDefault.Models;

namespace CityFy.Api.Clients;

public sealed class DumpClient(HttpClient http) : DownstreamClient(http, "Retrieve.Dump.Spotify"), IDumpClient
{
    /// <summary>Stops the user's imports, deletes the uploaded archives, extracted files and tasks.</summary>
    public Task<UserDataDeletion> DeleteUserDataAsync(string userId, CancellationToken ct) =>
        SendAsync<UserDataDeletion>(new HttpRequestMessage(HttpMethod.Delete, $"internal/users/{Uri.EscapeDataString(userId)}/data"), ct);

    public Task<UploadAccepted> UploadAsync(Stream zip, string fileName, string userId, CancellationToken ct)
    {
        var form = new MultipartFormDataContent
        {
            { new StreamContent(zip), "File", fileName },
            { new StringContent(userId), "UserId" }
        };
        return SendAsync<UploadAccepted>(new HttpRequestMessage(HttpMethod.Post, "internal/uploads") { Content = form }, ct);
    }

    public Task<TaskInfo> GetUploadAsync(string uploadId, CancellationToken ct) =>
        SendAsync<TaskInfo>(new HttpRequestMessage(HttpMethod.Get, $"internal/uploads/{Uri.EscapeDataString(uploadId)}"), ct);

    public Task<JsonElement> CancelUploadAsync(string uploadId, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, $"internal/uploads/{Uri.EscapeDataString(uploadId)}/cancel"), ct);

    public Task<JsonElement> ListUploadsAsync(int page, int pageSize, CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Get, $"internal/uploads?page={page}&pageSize={pageSize}"), ct);
}
