using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>Hints about an account that works but may not be receiving plays (pure rule).</summary>
public interface ILastFmAccountHintSubService
{
    /// <summary>
    /// LASTFM_SPOTIFY_NOT_SCROBBLING when the account is Active and no scrobble arrived for
    /// NotScrobblingDays since the link or the last scrobble; otherwise null. Asked when the user
    /// looks at their account, so "the user is active" is implied.
    /// </summary>
    string? WarningOf(LastFmAccount account, DateTime now);
}
