namespace CityFy.Retrieve.Spotify.SubServices;

public interface ISpotifyAuthorizeUrlSubService
{
    /// <summary>Spotify consent page URL (configured client, scopes and redirect URI).</summary>
    string Build(string state);
}