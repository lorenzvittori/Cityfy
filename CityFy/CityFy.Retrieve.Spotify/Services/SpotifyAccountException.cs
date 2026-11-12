using ServiceDefault.Errors;

namespace CityFy.Retrieve.Spotify.Services;

/// <summary>The linked account cannot be used: not linked (404) or must log in again (401).</summary>
public sealed class SpotifyAccountException(string code, string message, int statusCode)
    : CityFyException(code, message, statusCode)
{
    public static SpotifyAccountException NotLinked(string userId) =>
        new(ErrorCodes.SpotifyAccountNotLinked, $"Spotify account '{userId}' is not linked: log in via /debug/spotify/login", StatusCodes.Status404NotFound);

    public static SpotifyAccountException ReauthRequired(string message) =>
        new(ErrorCodes.SpotifyReauthRequired, message, StatusCodes.Status401Unauthorized);
}
