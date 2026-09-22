using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace CityFy.Retrieve.MusicBrainz.Clients;

public class MusicBrainzClient : IMusicBrainzClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MusicBrainzClient> _logger;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxArtists;
    private readonly int _top;
    private const int MusicBrainzMaxLimit = 100; // Highest allowed 'limit' for MusicBrainz artist search

    // HttpClient and ILogger are provided by DI. Semaphore limits parallel requests to MusicBrainz.
    public MusicBrainzClient(HttpClient http, ILogger<MusicBrainzClient> logger, IConfiguration configuration)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CityFyRetrieve/1.0 (contact: no-reply@example.com)");
        _semaphore = new SemaphoreSlim(4); // allow up to 4 concurrent calls to MB
        // Always use the maximum allowed by MusicBrainz for artist search page size
        _maxArtists = MusicBrainzMaxLimit;
        // default for top related tags (can be adjusted in code if needed)
        _top = 20;
    }

    public async Task<IEnumerable<string>> GetArtistTagsAsync(string artistMbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistMbId))
            return Array.Empty<string>();
        var url = $"https://musicbrainz.org/ws/2/artist/{artistMbId}?inc=tags&fmt=json";
        const int maxRetries = 4;
        int attempt = 0;
        var backoff = TimeSpan.FromSeconds(1);

        while (attempt < maxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("Getting tags for artist {ArtistId} (attempt {Attempt})", artistMbId, attempt);
                // Use GetAsync so we can inspect status codes and avoid EnsureSuccessStatusCode throwing immediately
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MusicBrainz returned {Status} for artist {ArtistId} (attempt {Attempt})", resp.StatusCode, artistMbId, attempt);
                    // Retry on server errors (5xx) or 429 Too Many Requests
                    if ((int)resp.StatusCode >= 500 || resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // fallthrough to retry
                    }
                    else
                    {
                        // client error or other status: do not retry
                        return Array.Empty<string>();
                    }
                }
                else
                {
                    var dto = await resp.Content.ReadFromJsonAsync<MusicBrainzArtistDto>(cancellationToken: cancellationToken);
                    if (dto?.tags == null)
                    {
                        _logger.LogInformation("No tags found for artist {ArtistId}", artistMbId);
                        return Array.Empty<string>();
                    }

                    return dto.tags.Select(t => t.name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HTTP error getting tags for {ArtistId} (attempt {Attempt})", artistMbId, attempt);
                // will retry
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout getting tags for {ArtistId} (attempt {Attempt})", artistMbId, attempt);
                // will retry
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MusicBrainzClient error for {ArtistId}", artistMbId);
                return Array.Empty<string>();
            }
            finally
            {
                try { _semaphore.Release(); } catch { }
            }

            if (attempt < maxRetries)
            {
                // exponential backoff with jitter
                var jitter = TimeSpan.FromMilliseconds(new Random().Next(0, 200));
                var delay = backoff + jitter;
                _logger.LogInformation("Retrying after {Delay} for artist {ArtistId}", delay, artistMbId);
                await Task.Delay(delay, cancellationToken);
                backoff = backoff * 2;
            }
        }

        _logger.LogWarning("Giving up getting tags for artist {ArtistId} after {Attempts} attempts", artistMbId, attempt);
        return Array.Empty<string>();
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

    public async Task<IEnumerable<(string Tag, int Count)>> GetRelatedTagsAsync(
        string tag,
        int pageDelaySeconds = 1,
        Func<int, IDictionary<string, int>, CancellationToken, Task>? onPageAggregatedAsync = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<(string, int)>();

        try
        {
            _logger.LogInformation("Searching artists for tag {Tag} (maxArtists={MaxArtists})", tag, _maxArtists);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int fetched = 0;
            int pageSize = Math.Min(25, Math.Max(1, _maxArtists));
            int offset = 0;
            int pageNumber = 1;

            while (fetched < _maxArtists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) Cerca una pagina di artisti etichettati con il tag
                var q = System.Net.WebUtility.UrlEncode($"tag:{tag}");
                var searchUrl = $"https://musicbrainz.org/ws/2/artist?query={q}&fmt=json&limit={pageSize}&offset={offset}";
                var searchDto = await _http.GetFromJsonAsync<ArtistSearchDto>(searchUrl, cancellationToken: cancellationToken);
                var artistIds = searchDto?.artists?.Where(a => !string.IsNullOrWhiteSpace(a.id)).Select(a => a.id!).ToList() ?? new List<string>();

                if (artistIds.Count == 0)
                {
                    _logger.LogInformation("No more artists found for tag {Tag} at offset {Offset}", tag, offset);
                    break;
                }

                _logger.LogInformation("Found {Count} artists for tag {Tag} (offset={Offset})", artistIds.Count, tag, offset);

                // 2) Recupera i tag in parallelo rispettando il semaphore nel GetArtistTagsAsync
                var tasks = artistIds.Select(id => GetArtistTagsAsync(id, cancellationToken)).ToList();
                var results = await Task.WhenAll(tasks);

                for (int i = 0; i < artistIds.Count; i++)
                {
                    var tags = results[i] ?? Array.Empty<string>();
                    foreach (var t in tags)
                    {
                        if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (counts.ContainsKey(t)) counts[t]++; else counts[t] = 1;
                    }
                }

                fetched += artistIds.Count;
                offset += artistIds.Count;
                // Invoke per-page aggregated callback if provided
                if (onPageAggregatedAsync != null)
                {
                    // copy counts to a read-only dictionary snapshot
                    var snapshot = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
                    await onPageAggregatedAsync(pageNumber, snapshot, cancellationToken);
                }

                // Respect MusicBrainz rate limits: pause between pages to avoid DoS
                if (pageDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(pageDelaySeconds), cancellationToken);

                pageNumber++;
            }

            var result = counts.OrderByDescending(kv => kv.Value).Take(_top).Select(kv => (kv.Key, kv.Value)).ToList();
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
