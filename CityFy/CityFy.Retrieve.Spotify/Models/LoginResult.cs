namespace CityFy.Retrieve.Spotify.Models;

public enum LoginError
{
    None,
    InvalidState,
    TokenExchangeFailed
}

public sealed record LoginResult(LoginError Error, SpotifyAccount? Account = null)
{
    public bool Success => Error == LoginError.None;
}
