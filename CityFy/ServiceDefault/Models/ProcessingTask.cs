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
        FAILED
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
    }
}
