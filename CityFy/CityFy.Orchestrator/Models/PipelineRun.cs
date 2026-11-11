using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Orchestrator.Models;

public enum PipelineStatus
{
    /// <summary>Waiting for the debounce window to close (more imports may join).</summary>
    Pending,

    /// <summary>BuildCityRequested sent, waiting for CityBuilt / CityBuildFailed.</summary>
    Building,

    Done,
    Failed
}

/// <summary>
/// One execution of the pipeline for a user ('pipeline_runs'): the imports that triggered it
/// and the city build they caused. At most one Pending run per user at a time.
/// </summary>
public class PipelineRun
{
    [BsonId]
    public string RunId { get; set; } = null!;

    public string UserId { get; set; } = null!;

    [BsonRepresentation(BsonType.String)]
    public PipelineStatus Status { get; set; }

    public List<PipelineTrigger> Triggers { get; set; } = new();

    public List<string> Periods { get; set; } = new();

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    /// <summary>When the Pending run is started (debounce).</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime DueAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? StartedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>Technical detail of the failure.</summary>
    public string? Error { get; set; }

    /// <summary>Stable ErrorCodes value when Status is Failed (CITY_BUILD_FAILED, MUSICBRAINZ_UNAVAILABLE, PIPELINE_TIMEOUT, ...).</summary>
    public string? ErrorCode { get; set; }
}

public class PipelineTrigger
{
    /// <summary>"dump", "api" or "manual".</summary>
    public string Source { get; set; } = null!;

    public string SourceRef { get; set; } = null!;

    public int NewPlays { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime At { get; set; }
}
