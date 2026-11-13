namespace CityFy.Retrieve.MusicBrainz.Models;

/// <summary>In-memory result of reading the dump tables, ready to be written.</summary>
public sealed class MusicBrainzData
{
    public List<Genre> Genres { get; } = new();
    public List<GenreRelation> GenreRelations { get; } = new();
    public List<Artist> Artists { get; } = new();
    public List<ArtistGenre> ArtistGenres { get; } = new();
    public List<ArtistSpotifyLink> SpotifyLinks { get; } = new();
}