using AutoMapper;
using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Services
{
    public class SpotifyConverter : ISpotifyConverter
    {
        private readonly IMapper _mapper;

        public SpotifyConverter(IMapper mapper)
        {
            _mapper = mapper;
        }

        public IEnumerable<Track> ConvertTracks(IEnumerable<SpotifyTrackDto> apiTracks)
        {
            return apiTracks.Select(t => _mapper.Map<Track>(t)).ToList();
        }

        public IEnumerable<Album> ConvertAlbums(IEnumerable<SpotifyAlbumDto> apiAlbums)
        {
            return apiAlbums.Select(a => _mapper.Map<Album>(a)).ToList();
        }
    }
}
