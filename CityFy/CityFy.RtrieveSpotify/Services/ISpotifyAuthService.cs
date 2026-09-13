using System.Threading.Tasks;
using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Services
{
    public interface ISpotifyAuthService
    {
        string GetAuthorizationUrl();
        Task<TokenResponse?> ExchangeCodeAsync(string code);
    }
}
