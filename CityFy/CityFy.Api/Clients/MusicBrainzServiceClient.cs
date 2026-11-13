using ServiceDefault.Clients;
using System.Text.Json;

namespace CityFy.Api.Clients;

public sealed class MusicBrainzServiceClient(HttpClient http) : DownstreamClient(http, "Retrieve.MusicBrainz"), IMusicBrainzServiceClient
{
    public Task<JsonElement> GetImportStatusAsync(CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Get, "api/retrieve/status"), ct);

    public Task<JsonElement> CancelImportAsync(CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, "api/retrieve/cancel"), ct);

    public Task<JsonElement> StartImportAsync(CancellationToken ct) =>
        SendAsync<JsonElement>(new HttpRequestMessage(HttpMethod.Post, "api/retrieve"), ct);
}
