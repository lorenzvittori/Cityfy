namespace ServiceDefault.Errors;

/// <summary>
/// Stable error codes shared with the frontend (catalog for the UI: ERRORI_UI.md).
/// Codes never change meaning; new situations get new codes. The frontend maps each code to
/// the message shown to the user and never parses the technical "detail".
/// </summary>
public static class ErrorCodes
{
    // --- General ---------------------------------------------------------
    public const string InternalError = "INTERNAL_ERROR";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    /// <summary>Unknown endpoint/resource without a more specific code.</summary>
    public const string NotFound = "NOT_FOUND";
    /// <summary>No session (or its cookie expired): start again from the welcome page.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";
    /// <summary>The anonymous session still had a cookie but expired (or its data was deleted): its data is gone.</summary>
    public const string SessionExpired = "SESSION_EXPIRED";
    /// <summary>Logged in, but without the required role (e.g. admin pages as a normal user).</summary>
    public const string Forbidden = "FORBIDDEN";
    /// <summary>Other 4xx without a more specific code (e.g. method not allowed).</summary>
    public const string RequestRejected = "REQUEST_REJECTED";
    /// <summary>Too many requests from this network (or uploads by this user / on the server): retry after "retryAfterSeconds".</summary>
    public const string RateLimited = "RATE_LIMITED";

    // --- Upload of the Spotify export (Retrieve.Dump.Spotify) -------------
    public const string UploadFileMissing = "UPLOAD_FILE_MISSING";
    public const string UploadUserMissing = "UPLOAD_USER_MISSING";
    public const string UploadNotZip = "UPLOAD_NOT_ZIP";
    public const string UploadTooLarge = "UPLOAD_TOO_LARGE";
    public const string UploadInvalidZip = "UPLOAD_INVALID_ZIP";
    public const string UploadNotFound = "UPLOAD_NOT_FOUND";
    /// <summary>Zip without Streaming_History_*.json: probably the wrong export type.</summary>
    public const string UploadNoStreamingFiles = "UPLOAD_NO_STREAMING_FILES";
    public const string UploadUnreadableFiles = "UPLOAD_UNREADABLE_FILES";
    public const string UploadProcessingFailed = "UPLOAD_PROCESSING_FAILED";

    // --- Last.fm account, history import and reading (Retrieve.LastFm) ----
    /// <summary>No Last.fm account linked to this user.</summary>
    public const string LastFmNotLinked = "LASTFM_NOT_LINKED";
    /// <summary>Authorization refused or expired (token older than 60 minutes, or used twice), or access revoked on Last.fm: link again.</summary>
    public const string LastFmAuthFailed = "LASTFM_AUTH_FAILED";
    /// <summary>Last.fm does not answer or failed temporarily (also a problem with the API key of CityFy).</summary>
    public const string LastFmUnavailable = "LASTFM_UNAVAILABLE";
    /// <summary>Last.fm limits the requests of CityFy (error 29): reading pauses and resumes on its own.</summary>
    public const string LastFmRateLimited = "LASTFM_RATE_LIMITED";
    /// <summary>The user's plays are hidden on Last.fm (private profile).</summary>
    public const string LastFmUserPrivate = "LASTFM_USER_PRIVATE";
    public const string LastFmImportNotFound = "LASTFM_IMPORT_NOT_FOUND";
    /// <summary>
    /// Not an error, a hint on the account: no new scrobble for days. Spotify may no longer send the
    /// plays to Last.fm (connection to check in the Last.fm settings).
    /// </summary>
    public const string LastFmSpotifyNotScrobbling = "LASTFM_SPOTIFY_NOT_SCROBBLING";
    /// <summary>The import of the Last.fm history stopped because of an unexpected error.</summary>
    public const string LastFmImportFailed = "LASTFM_IMPORT_FAILED";

    // --- City (Catalog) -----------------------------------------------
    /// <summary>The catalog refresh or the generation of a city failed (unexpected error).</summary>
    public const string CityBuildFailed = "CITY_BUILD_FAILED";
    /// <summary>Date range of a city request: start after end, or start in the future.</summary>
    public const string CityInvalidRange = "CITY_INVALID_RANGE";
    /// <summary>The user's artist catalog was never built: the plays are still being imported.</summary>
    public const string CityCatalogMissing = "CITY_CATALOG_MISSING";

    // --- Generated cities (CityGenerator) ---------------------------------------
    /// <summary>The generated city does not exist (or belongs to another user).</summary>
    public const string CityNotFound = "CITY_NOT_FOUND";
    /// <summary>The user already has the maximum number of saved cities: delete one first.</summary>
    public const string CityLimitReached = "CITY_LIMIT_REACHED";
    /// <summary>Unknown map type (organic, grid, custom).</summary>
    public const string CityInvalidLayout = "CITY_INVALID_LAYOUT";
    /// <summary>Parameter string of a custom map not valid: "problems" lists the key and the RecipeProblemCodes value.</summary>
    public const string CityInvalidRecipe = "CITY_INVALID_RECIPE";
    /// <summary>City name too long.</summary>
    public const string CityInvalidName = "CITY_INVALID_NAME";
    /// <summary>Too many genres or subgenres in the request.</summary>
    public const string CityInvalidGenres = "CITY_INVALID_GENRES";
    public const string MusicBrainzUnavailable = "MUSICBRAINZ_UNAVAILABLE";

    // --- City warnings (CityModel.Warnings: the city exists but needs a note in the UI) ---
    public const string CityNoPlays = "CITY_NO_PLAYS";
    public const string CityGenresUnavailable = "CITY_GENRES_UNAVAILABLE";
    public const string CityLowCoverage = "CITY_LOW_COVERAGE";

    // --- Pipeline (Orchestrator) ------------------------------------------
    public const string PipelineRunNotFound = "PIPELINE_RUN_NOT_FOUND";
    public const string PipelineTimeout = "PIPELINE_TIMEOUT";

    // --- MusicBrainz administration (not shown to end users) --------------
    public const string MbRetrieveAlreadyRunning = "MB_RETRIEVE_ALREADY_RUNNING";
    public const string MbTooManyItems = "MB_TOO_MANY_ITEMS";
    public const string MbGenreNotFound = "MB_GENRE_NOT_FOUND";

    // --- Cancellation (admin) ------------------------------------------------
    /// <summary>Status code of an upload / Last.fm history import / city build / import stopped by an administrator.</summary>
    public const string ProcessCancelled = "PROCESS_CANCELLED";
    /// <summary>Cancel requested for something already finished (or not running).</summary>
    public const string ProcessNotCancellable = "PROCESS_NOT_CANCELLABLE";

    // --- Admin panel --------------------------------------------------------
    public const string AdminInvalidCredentials = "ADMIN_INVALID_CREDENTIALS";

    // --- User data ------------------------------------------------------------
    /// <summary>
    /// Data deletion stopped half-way (a service did not answer): some data may remain. Deleting
    /// again is safe and completes it.
    /// </summary>
    public const string UserDataDeletionIncomplete = "USER_DATA_DELETION_INCOMPLETE";
}
