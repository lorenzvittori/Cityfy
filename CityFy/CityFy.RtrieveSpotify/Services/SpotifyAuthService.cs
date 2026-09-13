using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using CityFy.RtrieveSpotify.Clients;
using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Services
{
    public class SpotifyAuthService : ISpotifyAuthService
    {
        private readonly IConfiguration _config;
        private readonly ISpotifyAuthClient _authClient;

        public SpotifyAuthService(IConfiguration config, ISpotifyAuthClient authClient)
        {
            _config = config;
            _authClient = authClient;
        }

        public string GetAuthorizationUrl()
        {
            var clientId = _config.GetValue<string>("Spotify:ClientId");
            var redirectUri = _config.GetValue<string>("Spotify:RedirectUri");
            var scopes = _config.GetValue<string>("Spotify:Scopes") ?? "user-library-read user-read-private";

            var state = Guid.NewGuid().ToString("N");

            var sb = new StringBuilder();
            sb.Append("https://accounts.spotify.com/authorize?");
            sb.Append($"response_type=code&client_id={Uri.EscapeDataString(clientId)}");
            sb.Append($"&scope={Uri.EscapeDataString(scopes)}");
            sb.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri)}");
            sb.Append($"&state={Uri.EscapeDataString(state)}");
            sb.Append("&show_dialog=true");

            return sb.ToString();
        }

        public async Task<TokenResponse?> ExchangeCodeAsync(string code)
        {
            var redirectUri = _config.GetValue<string>("Spotify:RedirectUri");
            return await _authClient.ExchangeCodeForTokenAsync(code, redirectUri);
        }
    }
}
