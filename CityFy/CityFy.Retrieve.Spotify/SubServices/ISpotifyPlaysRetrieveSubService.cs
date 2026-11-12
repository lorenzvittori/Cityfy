using CityFy.Retrieve.Spotify.Models;

namespace CityFy.Retrieve.Spotify.SubServices;

public interface ISpotifyPlaysRetrieveSubService
{
    /// <summary>
    /// Reads the recently played tracks and stores them in 'listening_events', reporting progress.
    /// With <paramref name="since"/> only newer plays ('after' cursor); without, everything Spotify
    /// still has (walking back with the 'before' cursor). Returns the new plays and the most recent one.
    /// </summary>
    Task<(int Inserted, DateTime? LastPlayedAt)> RetrieveAsync(RetrieveJob job, string accessToken, DateTime? since, CancellationToken ct);
}