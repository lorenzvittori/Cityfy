namespace CityFy.Retrieve.Spotify.Models
{
    public class SpotifyExternalUrlsDto
    {
        public string? Spotify { get; set; }
    }

    public class SpotifyImageDto
    {
        public int? Height { get; set; }
        public int? Width { get; set; }
        public string? Url { get; set; }
    }

    public class SpotifyArtistDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public SpotifyExternalUrlsDto? ExternalUrls { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
    }

    public class SpotifyTrackDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SpotifyAlbumDto? Album { get; set; }
        public IEnumerable<SpotifyArtistDto>? Artists { get; set; }
        public int? DurationMs { get; set; }
        public bool? Explicit { get; set; }
        public int? TrackNumber { get; set; }
        public int? DiscNumber { get; set; }
        public string? PreviewUrl { get; set; }
        public int? Popularity { get; set; }
        public SpotifyExternalUrlsDto? ExternalUrls { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
        public bool? IsLocal { get; set; }
    }

    public class SpotifyAlbumDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? AlbumType { get; set; }
        public string? ReleaseDate { get; set; }
        public string? ReleaseDatePrecision { get; set; }
        public int? TotalTracks { get; set; }
        public IEnumerable<SpotifyImageDto>? Images { get; set; }
        public IEnumerable<SpotifyArtistDto>? Artists { get; set; }
        public SpotifyExternalUrlsDto? ExternalUrls { get; set; }
        public string? Href { get; set; }
        public string? Uri { get; set; }
    }

    public class SpotifyUserDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
    }

    public class SpotifyContextDto
    {
        public string? Type { get; set; }
        public string? Uri { get; set; }
        public string? Href { get; set; }
    }

    // Item of /me/player/recently-played
    public class SpotifyPlayHistoryDto
    {
        public SpotifyTrackDto? Track { get; set; }
        public DateTime PlayedAt { get; set; }
        public SpotifyContextDto? Context { get; set; }
    }

    // Cursor-based paging object (recently-played)
    public class SpotifyCursorPageDto<T>
    {
        public List<T> Items { get; set; } = new();
        public string? Next { get; set; }
        public int? Limit { get; set; }
    }
}
