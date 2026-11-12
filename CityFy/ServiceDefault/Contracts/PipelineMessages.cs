namespace ServiceDefault.Contracts;

// Messages of the CityFy pipeline. The routing key is the type name (see MessagingTopology),
// so renaming a type is a breaking change for the queues bound to it.
//
//   Dump.Spotify   UploadReceived ──► (itself) import ──► ListeningImported
//   Spotify        scheduled/manual retrieve ──────────► ListeningImported
//   Orchestrator   ListeningImported ──► (debounce) ──► RefreshCatalogRequested
//   Catalog        RefreshCatalogRequested ──► user catalog refresh ──► UserCatalogUpdated | CatalogRefreshFailed
//   Orchestrator   UserCatalogUpdated | CatalogRefreshFailed ──► pipeline run Done | Failed
//   CityGenerator  UserCatalogUpdated ──► first city of a new user
//                  GenerateCityRequested (itself) ──► city model from Catalog ──► generated city Done | Failed

/// <summary>A zip export was uploaded and extracted: import it (consumed by Dump.Spotify itself).</summary>
public sealed record UploadReceived(string UploadId, string UserId);

/// <summary>New plays were stored in 'listening_events' for a user.</summary>
public sealed record ListeningImported(string UserId, string Source, string SourceRef, int NewPlays, DateTime ImportedAt);

/// <summary>
/// The MusicBrainz genre catalog was (re)imported: artists that had no genre may have one now, so
/// the orchestrator refreshes the artist catalog of every user with plays.
/// </summary>
public sealed record CatalogUpdated(string? DumpName, DateTime UpdatedAt);

/// <summary>Command: rebuild the user's artist catalog (artist -> genre and subgenre) from all the plays.</summary>
public sealed record RefreshCatalogRequested(string RunId, string UserId);

/// <summary>
/// The user's artist catalog was rebuilt from all the plays: generated cities based on an older
/// CatalogVersion are outdated. RunId: the pipeline run that asked for it (null when refreshed
/// outside the pipeline, e.g. a debug call). Not to be confused with <see cref="CatalogUpdated"/>
/// (the MusicBrainz genre catalog).
/// </summary>
public sealed record UserCatalogUpdated(string? RunId, string UserId, string CatalogVersion, int Artists, int PlacedArtists, DateTime UpdatedAt);

/// <summary>ErrorCode: a ServiceDefault.Errors.ErrorCodes value; Error: technical detail.</summary>
public sealed record CatalogRefreshFailed(string RunId, string UserId, string ErrorCode, string Error);

/// <summary>A user asked for a city (stored Pending in 'generated_cities'): generate it (consumed by CityGenerator itself).</summary>
public sealed record GenerateCityRequested(string CityId, string UserId);