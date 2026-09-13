using CityFy.RtrieveSpotify.Clients;
using CityFy.RtrieveSpotify.Models;
using CityFy.RtrieveSpotify.Repositories;

namespace CityFy.RtrieveSpotify.Services
{
    public class SpotifyRetrieveService : ISpotifyRetrieveService
    {
        private readonly ISpotifyClient _client;
        private readonly ISpotifyRepository _repo;
        private readonly ISpotifyConverter _converter;
        private readonly int _pageSize;

        public SpotifyRetrieveService(ISpotifyClient client, ISpotifyRepository repo, ISpotifyConverter converter)
        {
            _client = client;
            _repo = repo;
            _converter = converter;
            _pageSize = 50;
        }

        public async Task StartRetrieveAsync(string token)
        {
            long? before = null;
            do
            {
                PagedResult<SpotifyTrackDto> page = await _client.GetTracksPageAsync(token, before, _pageSize);
                IEnumerable<Track> tracks = _converter.ConvertTracks(page.Items);
                await _repo.InsertTracksAsync(tracks);
                before = page.Before;
            } while (before is not null);
        }
    }
}
