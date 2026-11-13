using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Retrieve.LastFm.Models;

public enum LastFmAccountStatus
{
    /// <summary>Read periodically.</summary>
    Active,
    /// <summary>Session key refused (access revoked on Last.fm) or profile unreadable: not read until linked again.</summary>
    Suspended
}

/// <summary>
/// A Last.fm account linked to a CityFy user ('lastfm_accounts'). The session key is stored
/// encrypted (Data Protection) and never leaves the service.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class LastFmAccount
{
    public const string CollectionName = "lastfm_accounts";

    [BsonId]
    public string UserId { get; set; } = null!;

    /// <summary>Verified Last.fm user name (unique: one CityFy user per Last.fm account).</summary>
    public string Username { get; set; } = null!;

    public string ProtectedSessionKey { get; set; } = null!;

    [BsonRepresentation(BsonType.String)]
    public LastFmAccountStatus Status { get; set; }

    /// <summary>Why the account is Suspended (LASTFM_AUTH_FAILED, LASTFM_USER_PRIVATE).</summary>
    public string? ErrorCode { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LinkedAt { get; set; }

    /// <summary>
    /// Cursor of the polling: newest scrobble already read. Set to the link time, so the polling only
    /// reads what came after; everything before is the history import's job.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastScrobbleAt { get; set; }

    /// <summary>Last time the user was seen listening (now playing, or a new scrobble): drives the cadence.</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastListeningAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastPolledAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime NextPollAt { get; set; }

    /// <summary>Import of the history in progress or last done (resume point after a restart).</summary>
    public LastFmHistoryImport? HistoryImport { get; set; }
}

/// <summary>
/// The history is read newest first, in pages of PageSize, all with the same upper bound
/// (<see cref="Before"/>): new scrobbles do not shift the pages while the import runs.
/// </summary>
[BsonIgnoreExtraElements]
public sealed record LastFmHistoryImport(
    string ImportId,
    [property: BsonDateTimeOptions(Kind = DateTimeKind.Utc)] DateTime Before,
    int NextPage,
    int? TotalPages,
    int Inserted);
