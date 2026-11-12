using MongoDB.Bson.Serialization.Attributes;

namespace CityFy.Catalog.Models;

/// <summary>
/// Where an artist the user listened to goes in every city ('user_artist_placements'): district =
/// top-level genre, block = the artist's most voted genre inside it. Computed once per catalog
/// refresh (the MusicBrainz resolve is the slow part), read by every city build. Artists without
/// a match are stored too (not placed), so a city can report them.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class ArtistPlacement
{
    [BsonId]
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>Version of the catalog this placement belongs to (<see cref="UserCatalog.Version"/>).</summary>
    public string CatalogVersion { get; set; } = null!;

    /// <summary>Artist name as in the plays (key of the aggregation).</summary>
    public string ArtistName { get; set; } = null!;

    /// <summary>MusicBrainz artist; null when the artist could not be matched.</summary>
    public string? Mbid { get; set; }

    public string? Name { get; set; }
    public string? SpotifyArtistId { get; set; }

    /// <summary>Genre names of the artist, most voted first.</summary>
    public List<string> Genres { get; set; } = new();

    public int? DistrictGenreId { get; set; }
    public string? DistrictName { get; set; }
    public string? DistrictColor { get; set; }
    public int? BlockGenreId { get; set; }
    public string? BlockName { get; set; }

    [BsonIgnore]
    public bool IsPlaced => Mbid != null && DistrictGenreId != null && BlockGenreId != null;

    public static string BuildId(string userId, string catalogVersion, string artistName) => $"{userId}|{catalogVersion}|{artistName}";
}
