using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.Clients
{
    public class SpotifyAuthClient : ISpotifyAuthClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<SpotifyAuthClient> _logger;
        private readonly SpotifyOptions _options;

        public SpotifyAuthClient(HttpClient http, IOptions<SpotifyOptions> options, ILogger<SpotifyAuthClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, CancellationToken ct = default) =>
            PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri
            }, ct);

        public Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default) =>
            PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }, ct);

        private async Task<TokenResponse?> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_options.AccountsBase.TrimEnd('/')}/api/token")
            {
                Content = new FormUrlEncodedContent(form)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Spotify token request ({GrantType}) failed -> {StatusCode}: {Body}",
                    form["grant_type"], (int)resp.StatusCode, body);
                return null;
            }

            return JsonSerializer.Deserialize<TokenResponse>(body);
        }
    }
}
