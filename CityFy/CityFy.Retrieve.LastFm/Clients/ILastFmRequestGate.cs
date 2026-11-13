namespace CityFy.Retrieve.LastFm.Clients;

/// <summary>
/// One gate for every Last.fm request of the service: Last.fm applies its limits per IP, so polling
/// and history imports share the same budget.
/// </summary>
public interface ILastFmRequestGate
{
    /// <summary>Waits for the next free slot (MaxRequestsPerSecond) and for the end of a rate-limit pause.</summary>
    Task WaitAsync(CancellationToken ct);

    /// <summary>Error 29: stops every request for a pause that doubles at each further 29 (up to the maximum).</summary>
    void Pause();

    /// <summary>A request went through: the next pause starts again from the minimum.</summary>
    void Succeeded();

    /// <summary>End of the current rate-limit pause (UTC), null when not paused.</summary>
    DateTime? PausedUntil { get; }
}
