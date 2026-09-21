namespace CityFy.Retrieve.MusicBrainz.Models
{
    public class RelatedRetrieveRequest
    {
        public string? Tag { get; set; }
        public int MaxArtists { get; set; } = 50;
        public int Top { get; set; } = 20;
    }
}
