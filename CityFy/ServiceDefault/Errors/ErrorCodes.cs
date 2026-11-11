namespace ServiceDefault.Errors
{
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
        /// <summary>No session (or expired): log in with Spotify.</summary>
        public const string Unauthenticated = "UNAUTHENTICATED";
        /// <summary>Logged in, but without the required role (e.g. admin pages as a normal user).</summary>
        public const string Forbidden = "FORBIDDEN";
        /// <summary>Other 4xx without a more specific code (e.g. method not allowed).</summary>
        public const string RequestRejected = "REQUEST_REJECTED";

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

        // --- Spotify account and retrieve (Retrieve.Spotify) ------------------
        public const string SpotifyAuthDenied = "SPOTIFY_AUTH_DENIED";
        public const string SpotifyLoginExpired = "SPOTIFY_LOGIN_EXPIRED";
        public const string SpotifyTokenExchangeFailed = "SPOTIFY_TOKEN_EXCHANGE_FAILED";
        public const string SpotifyAccountNotLinked = "SPOTIFY_ACCOUNT_NOT_LINKED";
        /// <summary>Access revoked, token expired for good or no longer decryptable: log in again.</summary>
        public const string SpotifyReauthRequired = "SPOTIFY_REAUTH_REQUIRED";
        public const string SpotifyRateLimited = "SPOTIFY_RATE_LIMITED";
        public const string SpotifyUnavailable = "SPOTIFY_UNAVAILABLE";
        public const string SpotifyRetrieveFailed = "SPOTIFY_RETRIEVE_FAILED";
        public const string RetrieveJobNotFound = "RETRIEVE_JOB_NOT_FOUND";

        // --- City (Elaboration) -----------------------------------------------
        public const string CityInvalidPeriod = "CITY_INVALID_PERIOD";
        /// <summary>The city of that period was never built (yet).</summary>
        public const string CityNotReady = "CITY_NOT_READY";
        public const string CityBuildFailed = "CITY_BUILD_FAILED";
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
        /// <summary>Status code of an upload / Spotify sync / city build / import stopped by an administrator.</summary>
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
}
