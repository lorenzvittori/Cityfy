using ServiceDefault.Models;

namespace CityFy.Retrieve.LastFm.Models;

// Contract of the internal endpoints (internal/lastfm), used by the gateway.

public sealed record AuthorizeUrlResponse(string Url);

/// <summary>Token received by the gateway callback (?token=...), for the CityFy user to link.</summary>
public sealed record LinkRequest(string UserId, string Token);

/// <summary>ImportId: the history import started by this link (null when the history was already imported).</summary>
public sealed record LinkResponse(string UserId, string Username, string? ImportId);

public sealed record NowPlayingResponse(string Artist, string Track, string? Album, string? ArtistMbid, DateTime SeenAt);

/// <summary>A linked account, without the session key.</summary>
public sealed record LastFmAccountResponse(
    string UserId,
    string Username,
    string Status,
    string? ErrorCode,
    DateTime LinkedAt,
    DateTime LastScrobbleAt,
    DateTime? LastListeningAt,
    DateTime? LastPolledAt,
    DateTime NextPollAt,
    string? HistoryImportId,
    NowPlayingResponse? NowPlaying,
    string? Warning = null)
{
    /// <summary>Warning: a hint code (LASTFM_SPOTIFY_NOT_SCROBBLING), not an error.</summary>
    public static LastFmAccountResponse From(LastFmAccount a, NowPlaying? nowPlaying, string? warning = null) =>
        new(a.UserId, a.Username, a.Status.ToString(), a.ErrorCode, a.LinkedAt, a.LastScrobbleAt, a.LastListeningAt,
            a.LastPolledAt, a.NextPollAt, a.HistoryImport?.ImportId,
            nowPlaying is null ? null : new NowPlayingResponse(nowPlaying.Artist, nowPlaying.Track, nowPlaying.Album, nowPlaying.ArtistMbid, nowPlaying.SeenAt),
            warning);
}

public sealed record ImportAcceptedResponse(string ImportId);

public sealed record ImportCancelledResponse(string ImportId, string Status);

/// <summary>Status of a history import (same fields as the other jobs, see ProcessingTask).</summary>
public sealed record ImportResponse(
    string ImportId,
    string? UserId,
    string Status,
    int Inserted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode,
    string? Message,
    double? ProgressPercent,
    string? ProgressDetail,
    DateTime? UpdatedAt)
{
    public static ImportResponse From(ProcessingTask t) =>
        new(t.UploadId, t.UserId, t.Status.ToString(), t.Inserted, t.StartedAt, t.CompletedAt, t.ErrorCode, t.Message,
            t.ProgressPercent, t.ProgressDetail, t.UpdatedAt);
}
