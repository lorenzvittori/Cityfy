using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.Services
{
    public interface ISpotifyAuthService
    {
        /// <summary>Authorization URL with a fresh one-time 'state' (valid 10 minutes).</summary>
        string CreateAuthorizationUrl();

        /// <summary>
        /// Validates and consumes 'state', exchanges the code and links the account.
        /// </summary>
        Task<LoginResult> CompleteLoginAsync(string code, string? state, CancellationToken ct);
    }

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
}
