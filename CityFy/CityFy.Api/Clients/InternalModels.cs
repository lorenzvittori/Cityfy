namespace CityFy.Api.Clients;

// Shapes returned by the internal services (copied on purpose: services do not share assemblies,
// except the contracts in ServiceDefault such as CityModel).

public sealed record SpotifyLinkResult(string UserId, string? DisplayName);

public sealed record SpotifyAccountInfo(string UserId, string? DisplayName, DateTime LinkedAt, DateTime? LastRetrieveAt, DateTime? LastPlayedAt);

public sealed record JobAccepted(string JobId);

/// <summary>Status of an upload or of a Spotify retrieve (ServiceDefault.Models.ProcessingTask).</summary>
public sealed record TaskInfo(
    string UploadId,
    string? UserId,
    string Status,
    int Inserted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);

/// <summary>Spotify retrieve job as returned by internal/spotify/jobs/{id}.</summary>
public sealed record SpotifyJobInfo(
    string JobId,
    string? UserId,
    string Status,
    int Inserted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);

public sealed record UploadAccepted(string Id);

public sealed record PipelineTriggerInfo(string Source, string SourceRef, int NewPlays, DateTime At);

public sealed record PipelineRunInfo(
    string RunId,
    string UserId,
    string Status,
    List<PipelineTriggerInfo> Triggers,
    List<string> Periods,
    DateTime CreatedAt,
    DateTime DueAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode);
