using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace CityFy.Retrieve.Spotify.SubServices;

public sealed class SpotifyLoginStateSubService(IMemoryCache cache) : ISpotifyLoginStateSubService
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public string Create()
    {
        // Unguessable one-time value bound to this login attempt.
        // In-memory: with more than one instance it must move to a shared store.
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        cache.Set(Key(state), true, StateLifetime);
        return state;
    }

    public bool TryConsume(string? state)
    {
        if (string.IsNullOrEmpty(state) || !cache.TryGetValue(Key(state), out _))
            return false;

        cache.Remove(Key(state));
        return true;
    }

    private static string Key(string state) => $"spotify:oauth-state:{state}";
}