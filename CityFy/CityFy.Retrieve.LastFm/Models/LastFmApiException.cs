namespace CityFy.Retrieve.LastFm.Models;

/// <summary>
/// Error returned by the Last.fm API ({"error": code, "message": ...}). Codes used by CityFy:
/// 4 authentication failed, 6 invalid parameters (e.g. unknown user), 8 operation failed,
/// 9 invalid session key, 10 invalid API key, 11 service offline, 16 temporary error,
/// 17 login required (private profile), 26 suspended API key, 29 rate limit exceeded.
/// </summary>
public sealed class LastFmApiException(int code, string message) : Exception($"Last.fm error {code}: {message}")
{
    public const int AuthenticationFailed = 4;
    public const int InvalidParameters = 6;
    public const int OperationFailed = 8;
    public const int InvalidSessionKey = 9;
    public const int InvalidApiKey = 10;
    public const int ServiceOffline = 11;
    public const int TemporaryError = 16;
    public const int LoginRequired = 17;
    public const int SuspendedApiKey = 26;
    public const int RateLimitExceeded = 29;

    public int Code { get; } = code;

    /// <summary>Worth retrying after a wait (the request itself is fine).</summary>
    public bool IsTransient => Code is OperationFailed or ServiceOffline or TemporaryError or RateLimitExceeded;
}
