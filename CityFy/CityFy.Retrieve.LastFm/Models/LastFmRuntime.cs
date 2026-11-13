namespace CityFy.Retrieve.LastFm.Models;

// In-memory and in-flow models of the service (not stored).

/// <summary>What the user is listening to, as last seen by the polling (memory only).</summary>
public sealed record NowPlaying(string Artist, string Track, string? Album, string? ArtistMbid, DateTime SeenAt);

/// <summary>A history import waiting in the queue.</summary>
public sealed record LastFmImportJob(string ImportId, string UserId);

/// <summary>
/// Result of reading the new scrobbles of an account. NewestScrobbleAt: the new cursor (null = nothing
/// new); it covers only what was actually read, so a backlog larger than MaxPollPages continues at
/// the next poll.
/// </summary>
public sealed record LastFmPollResult(int Inserted, DateTime? NewestScrobbleAt, LastFmNowPlaying? NowPlaying);

/// <summary>One page of the history stored: plays inserted and pages in total.</summary>
public sealed record LastFmHistoryPageResult(int Inserted, int TotalPages, int Total);
