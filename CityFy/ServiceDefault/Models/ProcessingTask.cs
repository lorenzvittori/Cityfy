using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.Models
{
    public enum ProcessingStatus
    {
        Pending,
        WORKING_IN_PROGRESS,
        DONE,
        FAILED,

        /// <summary>Stopped by an administrator (ErrorCode = PROCESS_CANCELLED).</summary>
        CANCELLED
    }

    public class ProcessingTask
    {
        [BsonId]
        public string UploadId { get; set; } = null!;

        // Owner of the data being processed (Spotify user id until a CityFy identity exists)
        public string? UserId { get; set; }

        [BsonRepresentation(BsonType.String)]
        public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

        public int Inserted { get; set; } = 0;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        public string? Message { get; set; }

        /// <summary>Stable ErrorCodes value when Status is FAILED (Message is the technical detail).</summary>
        public string? ErrorCode { get; set; }

        /// <summary>0-100 while WORKING_IN_PROGRESS, when the work can be measured (null = indeterminate).</summary>
        public double? ProgressPercent { get; set; }

        /// <summary>What is being done right now, e.g. "Streaming_History_Audio_2021.json (3/12)".</summary>
        public string? ProgressDetail { get; set; }

        /// <summary>Last progress update: a WORKING task not updated for long is probably stuck.</summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
