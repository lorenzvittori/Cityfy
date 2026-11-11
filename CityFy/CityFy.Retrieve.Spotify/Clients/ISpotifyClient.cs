using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Clients
{
    public interface ISpotifyClient
    {
        // GET /me — used to scope the play history to the owner of the token.
        Task<SpotifyUserDto> GetCurrentUserAsync(string token, CancellationToken ct);

        // GET /me/player/recently-played (scope: user-read-recently-played).
        // Spotify only keeps the last ~50 plays, so this is not a full history.
        // 'after' (unix ms, exclusive) wins over 'before' when both are given: Spotify accepts only one.
        Task<PagedResult<SpotifyPlayHistoryDto>> GetRecentlyPlayedPageAsync(string token, long? before, long? after, int limit, CancellationToken ct);
    }
}
