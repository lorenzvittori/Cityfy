using MongoDB.Bson.Serialization.Attributes;

namespace ServiceDefault.City;

/// <summary>
/// City generated for a user and a period ('city_models' collection), read by the City API.
/// District = macro-genre, block = genre, building = artist. Holds metrics, not geometry:
/// the layout is computed by the frontend, as in the city_generator prototype.
/// </summary>
public class CityModel
{
    [BsonId]
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>"30d", "6m", "1y" or "all".</summary>
    public string Period { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime GeneratedAt { get; set; }

    /// <summary>Start of the period (null for "all").</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? From { get; set; }

    public CitySummary Summary { get; set; } = new();

    /// <summary>
    /// ErrorCodes "CITY_*" warnings: the city exists but the UI should explain something
    /// (no plays in the period, genres not available, low coverage).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    public List<CityDistrict> Districts { get; set; } = new();

    /// <summary>Most played artists that could not be matched to a genre (diagnostics).</summary>
    public List<UnresolvedArtist> Unresolved { get; set; } = new();

    public static string BuildId(string userId, string period) => $"{userId}:{period}";
}

public class CitySummary
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

public class CityDistrict
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

public class CityBlock
{
    public string BlockId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int GenreId { get; set; }
    public long Score { get; set; }
    public List<CityBuilding> Buildings { get; set; } = new();
}

public class CityBuilding
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

public class UnresolvedArtist
{
    public string Name { get; set; } = null!;
    public string? SpotifyArtistUri { get; set; }
    public long Plays { get; set; }
}
