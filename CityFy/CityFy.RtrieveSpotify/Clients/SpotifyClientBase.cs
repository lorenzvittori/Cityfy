using System.Text.Json;
using System.Net.Http.Headers;

namespace CityFy.RtrieveSpotify.Clients
{
    public abstract class SpotifyClientBase
    {
        protected readonly string _apiBase;
        protected readonly int _pageSize;

        protected SpotifyClientBase(IConfiguration config)
        {
            _apiBase = config.GetValue<string>("Spotify:ApiBase") ?? "https://api.spotify.com/v1";
            _pageSize = config.GetValue<int>("Spotify:PageSize", 50);
        }

        protected async Task<IEnumerable<T>> GetPagedAsync<T>(string token, string relativePath, string itemProperty)
            where T : class
        {
            var result = new List<T>();

            using var http = new HttpClient();
            // Normalize Authorization header: accept either "Bearer <token>" or raw token value
            var authValue = token ?? string.Empty;
            if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                authValue = authValue.Substring("Bearer ".Length);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authValue);
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var url = relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? relativePath : $"{_apiBase}{relativePath}";

            while (!string.IsNullOrEmpty(url))
            {
                using var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"Spotify API error GET {url} -> {resp.StatusCode}: {body}");
                    break;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty(itemProperty, out var payload))
                        {
                            var raw = payload.GetRawText();
                            var dto = JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (dto != null) result.Add(dto);
                        }
                    }
                }

                url = root.GetProperty("next").GetString();
            }

            return result;
        }

        protected async Task<(IEnumerable<T> Items, string? Next)> GetPageAsync<T>(string token, string relativePath, string itemProperty)
            where T : class
        {
            var result = new List<T>();

            using var http = new HttpClient();
            var authValue = token ?? string.Empty;
            if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                authValue = authValue.Substring("Bearer ".Length);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authValue);
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var url = relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? relativePath : $"{_apiBase}{relativePath}";

            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"Spotify API error GET {url} -> {resp.StatusCode}: {body}");
                return (result, null);
            }

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty(itemProperty, out var payload))
                    {
                        var raw = payload.GetRawText();
                        var dto = JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (dto != null) result.Add(dto);
                    }
                }
            }

            var next = root.GetProperty("next").GetString();
            return (result, next);
        }
    }
}
