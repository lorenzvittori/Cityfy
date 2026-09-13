using CityFy.RtrieveSpotify.Models;

namespace CityFy.RtrieveSpotify.Clients
{
    public class SpotifyClient : SpotifyClientBase, ISpotifyClient
    {
        public SpotifyClient(IConfiguration config) : base(config)
        {
        }

        public async Task<IEnumerable<SpotifyTrackDto>> GetAllSavedTracksAsync(string token)
        {
            return (await GetPagedAsync<SpotifyTrackDto>(token, $"/me/tracks?limit={_pageSize}", "track")).ToList();
        }

        // Returns the user's recently played tracks (most recent first).
        // Requires scope: user-read-recently-played
        public async Task<IEnumerable<SpotifyTrackDto>> GetRecentlyPlayedAsync(string token)
        {
            // The recently-played endpoint returns items[] with a 'track' property which matches SpotifyTrackDto
            return (await GetPagedAsync<SpotifyTrackDto>(token, $"/me/player/recently-played?limit={_pageSize}", "track")).ToList();
        }

        public async Task<PagedResult<SpotifyTrackDto>> GetTracksPageAsync(string token, long? before, int limit)
        {
            // Build query: recently-played supports before cursor as milliseconds timestamp
            var url = $"/me/player/recently-played?limit={limit}";
            if (before.HasValue)
                url += $"&before={before.Value}";

            var (items, next) = await GetPageAsync<SpotifyTrackDto>(token, url, "track");

            long? nextBefore = null;
            if (!string.IsNullOrEmpty(next))
            {
                // parse 'before' query param from next url
                try
                {
                    var u = new Uri(next);
                    var q = System.Web.HttpUtility.ParseQueryString(u.Query);
                    var beforeStr = q.Get("before");
                    if (long.TryParse(beforeStr, out var parsed)) nextBefore = parsed;
                }
                catch
                {
                    // ignore parse errors
                }
            }

            return new PagedResult<SpotifyTrackDto>
            {
                Items = items,
                Before = nextBefore,
                NextUrl = next
            };
        }
    }
}
