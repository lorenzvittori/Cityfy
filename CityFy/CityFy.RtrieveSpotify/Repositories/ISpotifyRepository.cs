using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Repositories
{
    public interface ISpotifyRepository
    {
        Task InsertTracksAsync(IEnumerable<Track> tracks);
        Task InsertAlbumsAsync(IEnumerable<Album> albums);
    }
}
