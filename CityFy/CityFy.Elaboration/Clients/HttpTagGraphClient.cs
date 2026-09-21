using CityFy.Elaboration.Models;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CityFy.Elaboration.Clients;

public class HttpTagGraphClient : ITagGraphClient
{
    private readonly HttpClient _http;

    public HttpTagGraphClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IEnumerable<TagGraph>> GetTagGraphsPagedAsync(string baseUrl, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("baseUrl");
        var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/musicbrainz/graphs");
        request.Headers.Add("X-Page", page.ToString());
        request.Headers.Add("X-Page-Size", pageSize.ToString());

        var resp = await _http.SendAsync(request, cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Failed to get graphs: {resp.StatusCode}");

        var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        var list = await JsonSerializer.DeserializeAsync<IEnumerable<TagGraph>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return list ?? Enumerable.Empty<TagGraph>();
    }
}
