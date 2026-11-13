using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.Models;

/// <summary>Lifecycle of an upload import or a Spotify retrieve: Pending -> InProgress -> Done | Failed | Cancelled.</summary>
public enum ProcessingStatus
{
    Pending,
    InProgress,
    Done,
    Failed,

    /// <summary>Stopped by an administrator (ErrorCode = PROCESS_CANCELLED).</summary>
    Cancelled
}

/// <summary>Persisted status of a background job ('*_tasks' collections).</summary>
public sealed class ProcessingTask
{
    [BsonId]
    public string UploadId { get; set; } = null!;

    // Owner of the data being processed (Spotify user id until a CityFy identity exists)
    public string? UserId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public int Inserted { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public string? Message { get; set; }

    /// <summary>Stable ErrorCodes value when Status is Failed (Message is the technical detail).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>0-100 while InProgress, when the work can be measured (null = indeterminate).</summary>
    public double? ProgressPercent { get; set; }

    /// <summary>What is being done right now, e.g. "Streaming_History_Audio_2021.json (3/12)".</summary>
    public string? ProgressDetail { get; set; }

    /// <summary>Last progress update: an InProgress task not updated for long is probably stuck.</summary>
    public DateTime? UpdatedAt { get; set; }
}
