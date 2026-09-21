using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace CityFy.Retrieve.MusicBrainz.Clients;

public class MusicBrainzClient : IMusicBrainzClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MusicBrainzClient> _logger;
    private readonly SemaphoreSlim _semaphore;

    public MusicBrainzClient()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CityFyRetrieve/1.0 (contact: no-reply@example.com)");
    }

    public async Task<IEnumerable<string>> GetArtistTagsAsync(string artistMbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistMbId))
            return Array.Empty<string>();

        var url = $"https://musicbrainz.org/ws/2/artist/{artistMbId}?inc=tags&fmt=json";
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("Getting tags for artist {ArtistId}", artistMbId);
                var dto = await _http.GetFromJsonAsync<MusicBrainzArtistDto>(url, cancellationToken: cancellationToken);
                if (dto?.tags == null)
                {
                    _logger.LogInformation("No tags found for artist {ArtistId}", artistMbId);
                    return Array.Empty<string>();
                }

                return dto.tags.Select(t => t.name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MusicBrainzClient error for {ArtistId}", artistMbId);
            return Array.Empty<string>();
        }
    }

    private class MusicBrainzArtistDto
    {
        public List<MusicBrainzTag>? tags { get; set; }
    }

    private class MusicBrainzTag
    {
        public string? name { get; set; }
        public int? count { get; set; }
    }

    // DTO per ricerca artisti
    private class ArtistSearchDto
    {
        public List<SearchArtist>? artists { get; set; }
    }

    private class SearchArtist
    {
        public string? id { get; set; }
    }

    public async Task<IEnumerable<(string Tag, int Count)>> GetRelatedTagsAsync(string tag, int maxArtists = 50, int top = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<(string, int)>();

        try
        {
            _logger.LogInformation("Searching artists for tag {Tag} (maxArtists={MaxArtists})", tag, maxArtists);
            // 1) Cerca artisti etichettati con il tag (limit)
            var q = System.Net.WebUtility.UrlEncode($"tag:{tag}");
            var searchUrl = $"https://musicbrainz.org/ws/2/artist?query={q}&fmt=json&limit={maxArtists}";
            var searchDto = await _http.GetFromJsonAsync<ArtistSearchDto>(searchUrl, cancellationToken: cancellationToken);
            var artistIds = searchDto?.artists?.Where(a => !string.IsNullOrWhiteSpace(a.id)).Select(a => a.id!).ToList() ?? new List<string>();

            _logger.LogInformation("Found {Count} artists for tag {Tag}", artistIds.Count, tag);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 2) Recupera i tag in parallelo rispettando il semaphore nel GetArtistTagsAsync
            var tasks = artistIds.Select(id => GetArtistTagsAsync(id, cancellationToken)).ToList();
            var results = await Task.WhenAll(tasks);

            for (int i = 0; i < artistIds.Count; i++)
            {
                var id = artistIds[i];
                var tags = results[i] ?? Array.Empty<string>();
                foreach (var t in tags)
                {
                    if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (counts.ContainsKey(t)) counts[t]++; else counts[t] = 1;
                }
            }

            var result = counts.OrderByDescending(kv => kv.Value).Take(top).Select(kv => (kv.Key, kv.Value)).ToList();
            _logger.LogInformation("Related tags for {Tag}: {TopCount} entries", tag, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRelatedTagsAsync error for {Tag}", tag);
            return Array.Empty<(string, int)>();
        }
    }
}
