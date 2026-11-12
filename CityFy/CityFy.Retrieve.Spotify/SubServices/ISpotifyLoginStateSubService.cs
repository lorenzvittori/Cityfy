namespace CityFy.Retrieve.Spotify.SubServices;

/// <summary>One-time OAuth 'state' (CSRF protection), valid 10 minutes.</summary>
public interface ISpotifyLoginStateSubService
{
    string Create();

    /// <summary>True (and the state is removed) when it was issued and not used yet.</summary>
    bool TryConsume(string? state);
}