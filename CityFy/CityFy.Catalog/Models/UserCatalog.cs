using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Catalog.Models;

/// <summary>
/// Current artist catalog of a user ('user_catalogs', one document per user). Version changes on
/// every refresh: only the placements of the current version are read, so a refresh never shows
/// a half-written catalog.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class UserCatalog
{
    /// <summary>User id.</summary>
    [BsonId]
    public string UserId { get; set; } = null!;

    public string Version { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    public int Artists { get; set; }
    public int PlacedArtists { get; set; }
}
