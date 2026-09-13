namespace ServiceDefault.Models
{
    public class MongoOptions
    {
        public string ConnectionString { get; set; } = "mongodb://localhost:27017";
        public string Database { get; set; } = "cityfy";
        // Single results collection name where all streaming events are stored
        public string ResultsCollectionName { get; set; } = "streaming_history";

        // Collection to store processing tasks/status
        public string TaskCollectionName { get; set; } = "upload_tasks";

        // legacy prefix (not used when ResultsCollectionName is set)
        public string CollectionPrefix { get; set; } = "streaming_history_";
    }
}
