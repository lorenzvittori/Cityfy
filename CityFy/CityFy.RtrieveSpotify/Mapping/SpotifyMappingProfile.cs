using AutoMapper;
using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Mapping
{
    public class SpotifyMappingProfile : Profile
    {
        public SpotifyMappingProfile()
        {
            // Spotify DTO -> BL models
            CreateMap<SpotifyExternalUrlsDto, ExternalUrls>()
                .ForMember(dest => dest.Spotify, opt => opt.MapFrom(src => src.Spotify));

            CreateMap<SpotifyImageDto, Image>();
            CreateMap<SpotifyArtistDto, Artist>();
            CreateMap<SpotifyAlbumDto, Album>();
            CreateMap<SpotifyTrackDto, Track>();
        }
    }
}
