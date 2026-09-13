using System.Threading.Tasks;
using CityFy.RtrieveSpotify.Models;
namespace CityFy.RtrieveSpotify.Clients
{
    public interface ISpotifyAuthClient
    {
        Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, string redirectUri);
    }
}
