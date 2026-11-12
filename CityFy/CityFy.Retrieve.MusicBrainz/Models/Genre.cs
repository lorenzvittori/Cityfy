namespace CityFy.Retrieve.MusicBrainz.Models;

/// <summary>Official MusicBrainz genre. Id is the MusicBrainz row id (not generated).</summary>
public sealed class Genre
{
    public int Id { get; set; }

    /// <summary>MusicBrainz genre MBID.</summary>
    public string Gid { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Relations where this genre is entity0 ("this has subgenre X", "this influenced by X", ...).</summary>
    public ICollection<GenreRelation> OutgoingRelations { get; set; } = new List<GenreRelation>();

    /// <summary>Relations where this genre is entity1.</summary>
    public ICollection<GenreRelation> IncomingRelations { get; set; } = new List<GenreRelation>();
}
