namespace CityFy.Retrieve.MusicBrainz.Models;

public static class GenreRelationTypes
{
    /// <summary>From has subgenre To (From is the parent).</summary>
    public const string Subgenre = "subgenre";

    /// <summary>From was influenced by To.</summary>
    public const string InfluencedBy = "influenced by";

    /// <summary>From is a fusion of To.</summary>
    public const string FusionOf = "fusion of";
}

/// <summary>
/// Genre-genre relationship from MusicBrainz (l_genre_genre), read as
/// "From {RelationType} To", see <see cref="GenreRelationTypes"/>.
/// </summary>
public class GenreRelation
{
    public int Id { get; set; }

    public int FromGenreId { get; set; }
    public Genre FromGenre { get; set; } = null!;

    public int ToGenreId { get; set; }
    public Genre ToGenre { get; set; } = null!;

    public string RelationType { get; set; } = null!;
}
