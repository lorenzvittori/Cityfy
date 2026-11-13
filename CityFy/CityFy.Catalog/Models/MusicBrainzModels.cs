namespace CityFy.Catalog.Models;

// Contract of the Retrieve.MusicBrainz read API (api/artists/resolve, api/genres/graph).
// Kept as a copy on purpose: services do not reference each other's assemblies.

public sealed class ResolveArtistsRequest
{
    public List<string> SpotifyArtistIds { get; set; } = new();
    public List<string> Names { get; set; } = new();
    /// <summary>MusicBrainz artist ids given by the source of the plays (Last.fm).</summary>
    public List<string> Mbids { get; set; } = new();
}

public sealed record ResolvedGenre(int Id, string Name, int Votes);

public sealed record ResolvedArtist(
    string Input, string MatchType, int? ArtistId, string? Mbid, string? Name, List<ResolvedGenre> Genres,
    string? SpotifyArtistId = null);

public sealed record ResolveArtistsResponse(List<ResolvedArtist> BySpotifyId, List<ResolvedArtist> ByName, List<ResolvedArtist>? ByMbid = null);

public sealed record GenreNode(int Id, string Gid, string Name);

public sealed record GenreEdge(int FromGenreId, int ToGenreId, string Type);

public sealed record GenreGraph(List<GenreNode> Genres, List<GenreEdge> Relations);
