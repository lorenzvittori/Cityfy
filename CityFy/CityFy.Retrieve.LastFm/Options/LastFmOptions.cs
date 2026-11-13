using System.ComponentModel.DataAnnotations;

namespace CityFy.Retrieve.LastFm.Options;

/// <summary>
/// "LastFm" section, validated at startup: a missing ApiKey/SharedSecret stops the service with an
/// explicit error instead of failing at the first link. Terms of use: PIANO_LASTFM.md, phase 0.
/// </summary>
public sealed class LastFmOptions
{
    public const string SectionName = "LastFm";

    [Required, Url]
    public string ApiBase { get; set; } = "https://ws.audioscrobbler.com/2.0/";

    /// <summary>Page where the user authorizes CityFy (web auth flow).</summary>
    [Required, Url]
    public string AuthUrl { get; set; } = "https://www.last.fm/api/auth/";

    /// <summary>API key of the CityFy application on Last.fm.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Signs the authenticated calls (api_sig). Never in appsettings: user-secrets / parameter.</summary>
    [Required]
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Where Last.fm sends the browser back with ?token=... (the gateway callback).</summary>
    [Required, Url]
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>
    /// Requests per second of the whole service. Last.fm does not publish its limits and applies them
    /// per IP: this is a cautious ceiling, error 29 pauses everything anyway.
    /// </summary>
    [Range(0.1, 20)]
    public double MaxRequestsPerSecond { get; set; } = 4;

    /// <summary>Scrobbles per page (Last.fm maximum: 200).</summary>
    [Range(1, 200)]
    public int PageSize { get; set; } = 200;

    /// <summary>First pause after error 29; doubles on every further 29, up to MaxRateLimitPauseSeconds.</summary>
    [Range(1, 3600)]
    public int RateLimitPauseSeconds { get; set; } = 30;

    [Range(1, 3600)]
    public int MaxRateLimitPauseSeconds { get; set; } = 600;

    /// <summary>Attempts of a request before giving up (rate limit and temporary errors).</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 4;

    // --- Reading of the new scrobbles (adaptive cadence) ------------------------------------

    /// <summary>How often the polling loop looks for accounts due (0 = polling disabled).</summary>
    [Range(0, 300)]
    public int PollTickSeconds { get; set; } = 5;

    /// <summary>
    /// A user is "on a CityFy page" for this long after a UserActive (the gateway sends one per minute
    /// while the page is open): read at ListeningIntervalSeconds meanwhile.
    /// </summary>
    [Range(30, 3600)]
    public int ActiveWindowSeconds { get; set; } = 150;

    /// <summary>Interval while the user is listening (a track is "now playing") or has a CityFy page open.</summary>
    [Range(5, 3600)]
    public int ListeningIntervalSeconds { get; set; } = 15;

    /// <summary>Interval for a user who listened within RecentWindowMinutes.</summary>
    [Range(5, 3600)]
    public int RecentIntervalSeconds { get; set; } = 120;

    /// <summary>Interval for everybody else.</summary>
    [Range(5, 86400)]
    public int IdleIntervalSeconds { get; set; } = 900;

    [Range(1, 1440)]
    public int RecentWindowMinutes { get; set; } = 60;

    /// <summary>Accounts read per tick (the rest waits: the rate gate spreads the requests anyway).</summary>
    [Range(1, 1000)]
    public int PollBatchSize { get; set; } = 20;

    /// <summary>
    /// No new scrobble for this many days (since the link or the last scrobble): the account shows
    /// LASTFM_SPOTIFY_NOT_SCROBBLING, a hint that Spotify may no longer send the plays to Last.fm.
    /// </summary>
    [Range(1, 365)]
    public int NotScrobblingDays { get; set; } = 3;

    /// <summary>Pages read per poll: more than this many new scrobbles are read at the next poll.</summary>
    [Range(1, 50)]
    public int MaxPollPages { get; set; } = 5;
}
