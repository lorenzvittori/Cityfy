using CityFy.RtrieveSpotify.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CityFy.RtrieveSpotify.Clients
{
    public class SpotifyAuthClient : ISpotifyAuthClient
    {
        private readonly string _tokenEndpoint = "https://accounts.spotify.com/api/token";
        private readonly string _clientId;
        private readonly string _clientSecret;

        public SpotifyAuthClient(IConfiguration config)
        {
            _clientId = config.GetValue<string>("Spotify:ClientId") ?? string.Empty;
            _clientSecret = config.GetValue<string>("Spotify:ClientSecret") ?? string.Empty;
        }

        public async Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, string redirectUri)
        {
            using var http = new HttpClient();
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            };

            using var content = new FormUrlEncodedContent(form);
            using var resp = await http.PostAsync(_tokenEndpoint, content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var token = JsonSerializer.Deserialize<TokenResponse>(body, opts);
            return token;
        }
    }
}
