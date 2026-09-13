namespace CityFy.RtrieveSpotify.Models
{
    public class ExternalUrls
    {
        public string? Spotify { get; set; }
    }

    public class Image
    {
        public int? Height { get; set; }
        public int? Width { get; set; }
        public string? Url { get; set; }
    }

    public class Artist
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public ExternalUrls? ExternalUrls { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
    }

    public class Album
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? AlbumType { get; set; }
        public string? ReleaseDate { get; set; }
        public string? ReleaseDatePrecision { get; set; }
        public int? TotalTracks { get; set; }
        public IEnumerable<Image>? Images { get; set; }
        public IEnumerable<Artist>? Artists { get; set; }
        public ExternalUrls? ExternalUrls { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
    }

    public class Track
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Album? Album { get; set; }
        public IEnumerable<Artist>? Artists { get; set; }
        public int? DurationMs { get; set; }
        public bool? Explicit { get; set; }
        public int? TrackNumber { get; set; }
        public int? DiscNumber { get; set; }
        public string? PreviewUrl { get; set; }
        public int? Popularity { get; set; }
        public ExternalUrls? ExternalUrls { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
        public bool? IsLocal { get; set; }
    }
}
