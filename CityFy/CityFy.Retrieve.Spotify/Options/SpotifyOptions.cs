using System.ComponentModel.DataAnnotations;

namespace CityFy.Retrieve.Spotify.Options;

/// <summary>
/// "Spotify" configuration section, validated at startup (ValidateOnStart): a missing
/// ClientSecret stops the service immediately with an explicit error instead of failing
/// at the first login.
/// </summary>
public sealed class SpotifyOptions
{
    public const string SectionName = "Spotify";

    [Required]
    public string ApiBase { get; set; } = "https://api.spotify.com/v1";

    [Required]
    public string AccountsBase { get; set; } = "https://accounts.spotify.com";

    [Range(1, 50)]
    public int PageSize { get; set; } = 50;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Never in appsettings: user-secrets in development, env/parameter elsewhere.</summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    [Required, Url]
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Interval of the automatic retrieve for every linked account (0 = disabled). Spotify keeps
    /// only the last ~50 plays: 30 minutes covers about 50 tracks of continuous listening.
    /// </summary>
    [Range(0, 1440)]
    public int ScheduledRetrieveMinutes { get; set; } = 30;

    /// <summary>Least privilege: only what the retrieve needs.</summary>
    [Required]
    public string Scopes { get; set; } = "user-read-recently-played user-read-private";
}
