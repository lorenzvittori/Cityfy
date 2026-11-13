namespace CityFy.Api.Clients;

// Shapes returned by the internal services (copied on purpose: services do not share assemblies,
// except the contracts in ServiceDefault such as CityModel).

/// <summary>Status of an upload (ServiceDefault.Models.ProcessingTask).</summary>
public sealed record TaskInfo(
    string UploadId,
    string? UserId,
    string Status,
    int Inserted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);

public sealed record UploadAccepted(string Id);

/// <summary>internal/lastfm/link: the actual owner (existing user, or the new id proposed) and the history import started.</summary>
public sealed record LastFmLinkResult(string UserId, string Username, string? ImportId);

/// <summary>Status: Active | Suspended (ErrorCode: LASTFM_AUTH_FAILED, LASTFM_USER_PRIVATE).</summary>
public sealed record LastFmAccountInfo(
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
    LastFmNowPlayingInfo? NowPlaying = null,
    string? Warning = null);

/// <summary>Track playing now, as last seen by the polling of Retrieve.LastFm.</summary>
public sealed record LastFmNowPlayingInfo(string Artist, string Track, string? Album, string? ArtistMbid, DateTime SeenAt);

/// <summary>History import (internal/lastfm/imports/{id}).</summary>
public sealed record LastFmImportInfo(
    string ImportId,
    string? UserId,
    string Status,
    int Inserted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode,
    double? ProgressPercent,
    string? ProgressDetail);

public sealed record PipelineTriggerInfo(string Source, string SourceRef, int NewPlays, DateTime At);

public sealed record PipelineRunInfo(
    string RunId,
    string UserId,
    string Status,
    List<PipelineTriggerInfo> Triggers,
    DateTime CreatedAt,
    DateTime DueAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);
