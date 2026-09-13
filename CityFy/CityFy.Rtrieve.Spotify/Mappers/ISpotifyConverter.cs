using CityFy.RtrieveSpotify.Models;

namespace CityFy.Rtrieve.Spotify.Mappers
{
    public interface ISpotifyConverter
    {
        IEnumerable<Track> ConvertTracks(IEnumerable<SpotifyTrackDto> apiTracks);
        IEnumerable<Album> ConvertAlbums(IEnumerable<SpotifyAlbumDto> apiAlbums);
    }
}
