using System.ComponentModel.DataAnnotations;

namespace CityFy.Api.Options;

/// <summary>"Sessions" section: anonymous sessions (no account).</summary>
public sealed class AnonymousSessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>How long a session lives without visits. Every visit moves the expiry to now + Lifetime.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "30.00:00:00")]
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Minimum time between two renewals of the same session: avoids a database write (and a new
    /// cookie) on every request. Must be well below Lifetime.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan RenewAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Interval of the cleanup of expired sessions, in minutes (0 = disabled).</summary>
    [Range(0, 1440)]
    public int CleanupIntervalMinutes { get; set; } = 10;

    /// <summary>Sessions handled per cleanup round (the rest waits for the next round).</summary>
    [Range(1, 1000)]
    public int CleanupBatchSize { get; set; } = 50;
}
