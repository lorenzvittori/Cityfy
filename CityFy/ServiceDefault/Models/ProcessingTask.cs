using System;
using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.Models
{
    public class ProcessingTask
    {
        [BsonId]
        public string UploadId { get; set; } = null!;

        public string Status { get; set; } = "pending";

        public int Inserted { get; set; } = 0;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        public string? Message { get; set; }
    }
}
