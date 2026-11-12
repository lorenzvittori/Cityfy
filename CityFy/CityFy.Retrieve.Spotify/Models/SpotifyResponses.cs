using ServiceDefault.Models;

namespace CityFy.Retrieve.Spotify.Models;

// Responses of the internal/debug endpoints (consumed by CityFy.Api, see its InternalModels.cs).

public sealed record LinkRequest(string Code, string? State);

public sealed record AuthorizeUrlResponse(string Url);

public sealed record LinkResponse(string UserId, string? DisplayName);

public sealed record SpotifyAccountResponse(string UserId, string? DisplayName, DateTime LinkedAt, DateTime? LastRetrieveAt, DateTime? LastPlayedAt)
{
    public static SpotifyAccountResponse From(SpotifyAccount a) =>
        new(a.UserId, a.DisplayName, a.LinkedAt, a.LastRetrieveAt, a.LastPlayedAt);
}

public sealed record JobAcceptedResponse(string JobId);

public sealed record JobCancelledResponse(string JobId, string Status);

public sealed record RetrieveJobResponse(
    string JobId,
    string? UserId,
    string Status,
    int Inserted,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode,
    string? Message,
    double? ProgressPercent,
    string? ProgressDetail,
    DateTime? UpdatedAt)
{
    public static RetrieveJobResponse From(ProcessingTask t) =>
        new(t.UploadId, t.UserId, t.Status.ToString(), t.Inserted, t.StartedAt, t.CompletedAt, t.ErrorCode,
            t.Message, t.ProgressPercent, t.ProgressDetail, t.UpdatedAt);
}
