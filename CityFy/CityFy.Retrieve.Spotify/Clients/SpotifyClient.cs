using CityFy.Retrieve.Spotify.Models;
using CityFy.Retrieve.Spotify.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Spotify.Clients;

public sealed class SpotifyClient(HttpClient http, IOptions<SpotifyOptions> options, ILogger<SpotifyClient> logger)
    : SpotifyClientBase(http, options.Value, logger), ISpotifyClient
{
    public async Task<SpotifyUserDto> GetCurrentUserAsync(string token, CancellationToken ct)
    {
        var user = await GetAsync<SpotifyUserDto>(token, "/me", ct);
        if (string.IsNullOrEmpty(user?.Id))
            throw new InvalidOperationException("Spotify /me returned no user id");
        return user;
    }

    public async Task<PagedResult<SpotifyPlayHistoryDto>> GetRecentlyPlayedPageAsync(string token, long? before, long? after, int limit, CancellationToken ct)
    {
        var path = $"/me/player/recently-played?limit={Math.Clamp(limit, 1, 50)}";
        if (after.HasValue)
            path += $"&after={after.Value}";
        else if (before.HasValue)
            path += $"&before={before.Value}";

        var page = await GetAsync<SpotifyCursorPageDto<SpotifyPlayHistoryDto>>(token, path, ct);

        return new PagedResult<SpotifyPlayHistoryDto>
        {
            Items = page?.Items ?? new List<SpotifyPlayHistoryDto>(),
            Before = ParseBefore(page?.Next),
            NextUrl = page?.Next
        };
    }

    // The 'next' URL carries the 'before' cursor (unix ms) for the following page.
    private static long? ParseBefore(string? next)
    {
        if (string.IsNullOrEmpty(next) || !Uri.TryCreate(next, UriKind.Absolute, out var uri))
            return null;

        var beforeStr = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("before");
        return long.TryParse(beforeStr, out var parsed) ? parsed : null;
    }
}
