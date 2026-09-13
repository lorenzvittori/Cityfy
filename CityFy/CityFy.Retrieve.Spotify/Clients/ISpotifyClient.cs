using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Clients
{
    public interface ISpotifyClient
    {
        Task<IEnumerable<SpotifyTrackDto>> GetAllSavedTracksAsync(string token);
        // Returns the user's recently played tracks (most recent first).
        Task<IEnumerable<SpotifyTrackDto>> GetRecentlyPlayedAsync(string token);
        Task<PagedResult<SpotifyTrackDto>> GetTracksPageAsync(string token, long? before, int limit);
    }
}
