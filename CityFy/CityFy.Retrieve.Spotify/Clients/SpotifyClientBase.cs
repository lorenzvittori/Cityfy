using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CityFy.Retrieve.Spotify.Options;

namespace CityFy.Retrieve.Spotify.Clients
{
    public abstract class SpotifyClientBase
    {
        // Spotify returns snake_case JSON (duration_ms, played_at, ...): a case-insensitive
        // match alone does not map underscores, so the naming policy is required.
        protected static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        private const int MaxAttempts = 5;
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        protected readonly HttpClient _http;
        protected readonly string _apiBase;
        protected readonly int _pageSize;
        protected readonly ILogger _logger;

        protected SpotifyClientBase(HttpClient http, SpotifyOptions options, ILogger logger)
        {
            _http = http;
            _apiBase = options.ApiBase.TrimEnd('/');
            _pageSize = options.PageSize;
            _logger = logger;
        }

        /// <summary>
        /// GET on the Spotify Web API with retry on 429 (honouring Retry-After) and 5xx.
        /// Throws <see cref="HttpRequestException"/> when the call ultimately fails, so callers
        /// never mistake a partial result for a complete one.
        /// </summary>
        protected async Task<T?> GetAsync<T>(string token, string pathOrUrl, CancellationToken ct)
        {
            var url = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : $"{_apiBase}{pathOrUrl}";
            var bearer = NormalizeToken(token);

            for (var attempt = 1; ; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (resp.IsSuccessStatusCode)
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
                }

                var retriable = resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500;
                var delay = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));

                if (retriable && attempt < MaxAttempts && delay <= MaxRetryDelay)
                {
                    _logger.LogWarning("Spotify API GET {Url} -> {StatusCode}, retry {Attempt}/{Max} in {Delay}",
                        url, (int)resp.StatusCode, attempt, MaxAttempts - 1, delay);
                    await Task.Delay(delay, ct);
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("Spotify API error GET {Url} -> {StatusCode}: {Body}", url, (int)resp.StatusCode, body);
                throw new HttpRequestException(
                    $"Spotify API GET {url} failed with {(int)resp.StatusCode}: {body}", null, resp.StatusCode);
            }
        }

        // Accept either "Bearer <token>" or the raw token value.
        private static string NormalizeToken(string? token)
        {
            var value = token?.Trim() ?? string.Empty;
            return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value["Bearer ".Length..].Trim() : value;
        }
    }
}
