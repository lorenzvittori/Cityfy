namespace ServiceDefault.Models;

/// <summary>
/// "Mongo" section: collection names only. The connection comes from
/// "ConnectionStrings:cityfy", injected by the AppHost (see MongoExtensions.AddCityFyMongo).
/// </summary>
public sealed class MongoOptions
{
    // Collection to store upload processing tasks/status (Retrieve.Dump.Spotify)
    public string TaskCollectionName { get; set; } = "upload_tasks";

    // Prefix of the legacy per-upload collections, read only by the migration endpoint
    public string CollectionPrefix { get; set; } = "streaming_history_";
}
