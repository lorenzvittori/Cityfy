using System.Threading.Tasks;

namespace CityFy.RtrieveSpotify.Services
{
    public interface ISpotifyRetrieveService
    {
        Task StartRetrieveAsync(string token);
    }
}
