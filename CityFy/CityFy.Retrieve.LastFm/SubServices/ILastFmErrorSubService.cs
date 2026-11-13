namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>Technical errors → stable ErrorCodes (pure).</summary>
public interface ILastFmErrorSubService
{
    /// <summary>
    /// Last.fm 29 → LASTFM_RATE_LIMITED; 4, 6, 9 and an undecryptable key → LASTFM_AUTH_FAILED;
    /// 17 → LASTFM_USER_PRIVATE; 8, 10, 11, 16, 26, network, timeout and unreadable answers →
    /// LASTFM_UNAVAILABLE; anything else → <paramref name="fallback"/>.
    /// </summary>
    string ToErrorCode(Exception error, string fallback);

    /// <summary>The account itself cannot be read (stop polling it until it is linked again).</summary>
    bool IsAccountProblem(string errorCode);

    /// <summary>Error of the CityFy API key (invalid or suspended): an administrator must act.</summary>
    bool IsConfigurationProblem(Exception error);
}
