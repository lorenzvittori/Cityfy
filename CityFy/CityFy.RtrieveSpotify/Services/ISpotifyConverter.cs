using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Services
{
    public interface ISpotifyConverter
    {
        IEnumerable<Track> ConvertTracks(IEnumerable<SpotifyTrackDto> apiTracks);
        IEnumerable<Album> ConvertAlbums(IEnumerable<SpotifyAlbumDto> apiAlbums);
    }
}
