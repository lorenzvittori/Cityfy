namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>When to read an account again (pure rule).</summary>
public interface ILastFmPollCadenceSubService
{
    /// <summary>
    /// ListeningIntervalSeconds while a track is playing or a CityFy page is open (<paramref name="fast"/>),
    /// RecentIntervalSeconds if the user listened within RecentWindowMinutes, IdleIntervalSeconds otherwise.
    /// </summary>
    DateTime NextPollAt(DateTime now, bool fast, DateTime? lastListeningAt);
}
