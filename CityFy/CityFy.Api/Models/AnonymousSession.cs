using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Api.Models;

/// <summary>
/// Lifecycle of an anonymous session: Active until it expires, Expired while its data is being
/// deleted on every service (stays Expired while a service does not answer, the cleanup retries),
/// Deleted once nothing is left. Logging in with an account moves the data instead: Transferring
/// (never expires, retried until complete), then Transferred.
/// </summary>
public enum AnonymousSessionStatus
{
    Active,
    Expired,
    Deleted,
    Transferring,
    Transferred
}

/// <summary>
/// A visitor without an account ('anonymous_sessions', owned by the gateway). The cookie only carries
/// the id: this document is the truth about whether the session is still valid.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AnonymousSession
{
    public const string CollectionName = "anonymous_sessions";

    /// <summary>Random 128-bit id (hex). The owner id sent to the services is "anon_{Id}".</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    /// <summary>Last time the expiry was moved forward (renewals are throttled, see AnonymousSessionOptions.RenewAfter).</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime RenewedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAt { get; set; }

    [BsonRepresentation(BsonType.String)]
    public AnonymousSessionStatus Status { get; set; }

    /// <summary>When the session ended (data deleted or moved); a TTL index removes the document some days later.</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeletedAt { get; set; }

    /// <summary>Account that receives the data (Transferring / Transferred).</summary>
    public string? TransferTo { get; set; }
}

/// <summary>Outcome of checking the session of a request.</summary>
public enum SessionCheckOutcome
{
    /// <summary>Active, expiry unchanged.</summary>
    Valid,
    /// <summary>Active, expiry moved forward: the cookie must be issued again.</summary>
    Renewed,
    /// <summary>Unknown, expired or deleted: the request is not authenticated (SESSION_EXPIRED).</summary>
    Invalid
}

public sealed record SessionCheck(SessionCheckOutcome Outcome, DateTime? ExpiresAt);

/// <summary>Result of one round of the cleanup of expired sessions.</summary>
public sealed record SessionCleanupResult(int Deleted, int Failed);

/// <summary>Result of moving the data of an anonymous session: Failed lists the services to retry.</summary>
public sealed record SessionTransferResult(bool Complete, IReadOnlyList<string> Failed);
