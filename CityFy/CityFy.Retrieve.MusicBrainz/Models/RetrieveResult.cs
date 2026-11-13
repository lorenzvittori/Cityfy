namespace CityFy.Retrieve.MusicBrainz.Models;

public sealed class RetrieveResult
{
    public string? DumpName { get; set; }
    public int Genres { get; set; }
    public int GenreRelations { get; set; }
    public int Artists { get; set; }
    public int ArtistGenres { get; set; }
    public int SpotifyLinks { get; set; }
    public TimeSpan Duration { get; set; }
}
