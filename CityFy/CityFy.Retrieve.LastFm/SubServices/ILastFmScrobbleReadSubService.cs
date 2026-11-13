using CityFy.Retrieve.LastFm.Models;

namespace CityFy.Retrieve.LastFm.SubServices;

/// <summary>Reads scrobbles from Last.fm and stores them as plays ('listening_events', source lastfm).</summary>
public interface ILastFmScrobbleReadSubService
{
    /// <summary>
    /// One page of the history, all scrobbles before <see cref="LastFmHistoryImport.Before"/> (newest first,
    /// stable while the import runs). Storing a page twice is harmless (idempotent upsert).
    /// </summary>
    Task<LastFmHistoryPageResult> ReadHistoryPageAsync(LastFmAccount account, string sessionKey, LastFmHistoryImport import, CancellationToken ct);

    /// <summary>
    /// The scrobbles after the account cursor, and the track playing now. When there are more than
    /// MaxPollPages pages, only the oldest ones are read: the cursor moves to the newest of them and
    /// the rest comes at the next poll, so nothing is skipped.
    /// </summary>
    Task<LastFmPollResult> ReadNewAsync(LastFmAccount account, string sessionKey, string sourceRef, CancellationToken ct);
}
