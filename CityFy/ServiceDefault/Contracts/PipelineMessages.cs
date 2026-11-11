namespace ServiceDefault.Contracts
{
    // Messages of the CityFy pipeline. The routing key is the type name (see MessagingTopology),
    // so renaming a type is a breaking change for the queues bound to it.
    //
    //   Dump.Spotify   UploadReceived ──► (itself) import ──► ListeningImported
    //   Spotify        scheduled/manual retrieve ──────────► ListeningImported
    //   Orchestrator   ListeningImported ──► (debounce) ──► BuildCityRequested
    //   Elaboration    BuildCityRequested ──► CityBuilt | CityBuildFailed
    //   Orchestrator   CityBuilt | CityBuildFailed ──► pipeline run DONE | FAILED

    /// <summary>A zip export was uploaded and extracted: import it (consumed by Dump.Spotify itself).</summary>
    public sealed record UploadReceived(string UploadId, string UserId);

    /// <summary>New plays were stored in 'listening_events' for a user.</summary>
    public sealed record ListeningImported(string UserId, string Source, string SourceRef, int NewPlays, DateTime ImportedAt);

    /// <summary>Command: (re)build the user's city for the given periods.</summary>
    public sealed record BuildCityRequested(string RunId, string UserId, IReadOnlyList<string> Periods);

    public sealed record CityBuilt(string RunId, string UserId, IReadOnlyList<string> Periods, DateTime BuiltAt);

    /// <summary>ErrorCode: a ServiceDefault.Errors.ErrorCodes value; Error: technical detail.</summary>
    public sealed record CityBuildFailed(string RunId, string UserId, string ErrorCode, string Error);
}
