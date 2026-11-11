using CityFy.Retrieve.Spotify.Models;
using ServiceDefault.Listening;

namespace CityFy.Retrieve.Spotify.Mappers
{
    public interface ISpotifyConverter
    {
        /// <summary>
        /// Maps recently-played items to shared listening events. Items without a
        /// spotify:track URI (local files, episodes) are skipped.
        /// </summary>
        IReadOnlyList<ListeningEvent> ToListeningEvents(IEnumerable<SpotifyPlayHistoryDto> plays, string userId, string jobId);
    }
}
