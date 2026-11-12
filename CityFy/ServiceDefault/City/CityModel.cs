using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.City;

/// <summary>
/// Data of a city, computed by Catalog for a date range and genres (api/catalog/city-model) and
/// stored by CityGenerator inside the generated city, next to its map. District = macro-genre,
/// block = genre, building = artist. Holds metrics, not geometry (the map is a CityLayout).
/// </summary>
// Cities saved before the period cities were removed still have _id and Period: ignored
[BsonIgnoreExtraElements]
public sealed class CityModel
{
    public string UserId { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime GeneratedAt { get; set; }

    /// <summary>Start of the range (null = from the first play).</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? From { get; set; }

    /// <summary>End of the range, exclusive (null = up to <see cref="GeneratedAt"/>).</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? To { get; set; }

    public CitySummary Summary { get; set; } = new();

    /// <summary>
    /// ErrorCodes "CITY_*" warnings: the city exists but the UI should explain something
    /// (no plays in the range, genres not available, low coverage).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    public List<CityDistrict> Districts { get; set; } = new();

    /// <summary>Most played artists that could not be matched to a genre (diagnostics).</summary>
    public List<UnresolvedArtist> Unresolved { get; set; } = new();
}

public sealed class CitySummary
{
    public long TotalPlays { get; set; }
    public long QualifiedPlays { get; set; }
    public double ListeningHours { get; set; }
    public int TotalArtists { get; set; }
    public int PlacedArtists { get; set; }

    /// <summary>Share (0-100) of qualified plays whose artist was placed in the city.</summary>
    public double CoveragePercent { get; set; }

    public List<string> TopGenres { get; set; } = new();
}

public sealed class CityDistrict
{
    public string DistrictId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int GenreId { get; set; }
    public string Color { get; set; } = null!;

    /// <summary>Artists in the district relative to the most populated district (0-1).</summary>
    public double Density { get; set; }

    /// <summary>Share (0-100) of the placed qualified plays.</summary>
    public double ListeningIntensity { get; set; }

    public long Score { get; set; }
    public List<CityBlock> Blocks { get; set; } = new();
}

public sealed class CityBlock
{
    public string BlockId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int GenreId { get; set; }
    public long Score { get; set; }
    public List<CityBuilding> Buildings { get; set; } = new();
}

public sealed class CityBuilding
{
    /// <summary>MusicBrainz artist MBID.</summary>
    public string BuildingId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? SpotifyArtistUri { get; set; }
    public long Plays { get; set; }
    public long QualifiedPlays { get; set; }
    public double ListeningMinutes { get; set; }

    /// <summary>Log scale of the qualified plays, between CityOptions.MinHeight and MaxHeight.</summary>
    public double Height { get; set; }

    public double Width { get; set; }
    public string Color { get; set; } = null!;

    /// <summary>Genre votes of the artist (top first), for tooltips/filters.</summary>
    public List<string> Genres { get; set; } = new();
}

public sealed class UnresolvedArtist
{
    public string Name { get; set; } = null!;
    public string? SpotifyArtistUri { get; set; }
    public long Plays { get; set; }
}
