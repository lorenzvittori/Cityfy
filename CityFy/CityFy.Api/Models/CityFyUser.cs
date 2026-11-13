using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Api.Models;

/// <summary>
/// A CityFy account ('users', owned by the gateway). Only users who log in with Last.fm have one;
/// visitors without an account are anonymous sessions. The Last.fm user name is the identity: the
/// same Last.fm account always logs in the same CityFy user.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CityFyUser
{
    public const string CollectionName = "users";

    /// <summary>"u_" + 24 hex characters: the owner id the internal services receive.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string LastFmUsername { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastLoginAt { get; set; }
}

/// <summary>A login with Last.fm: who logged in and whether the data of an anonymous session came along.</summary>
public sealed record AccountLoginResult(string UserId, string Username, bool IsNew, SessionTransferResult? Transfer);
