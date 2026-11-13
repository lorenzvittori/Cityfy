namespace CityFy.Retrieve.MusicBrainz.Models;

public static class ArtistMatchTypes
{
    /// <summary>Matched through the Spotify artist link stored in MusicBrainz (reliable).</summary>
    public const string SpotifyUrl = "spotify-url";

    /// <summary>Matched by MusicBrainz artist id, given by the source (Last.fm): exact.</summary>
    public const string Mbid = "mbid";

    /// <summary>Matched by name (case-insensitive); among homonyms the one with most genre votes wins.</summary>
    public const string Name = "name";

    public const string None = "none";
}

public sealed class ResolveArtistsRequest
{
    /// <summary>Spotify artist ids, raw ("4tZwfgrHOc3mvqYlEYSvVi") or URI ("spotify:artist:...").</summary>
    public List<string>? SpotifyArtistIds { get; set; }

    /// <summary>Artist names, used when the Spotify id is unknown (plays from the export).</summary>
    public List<string>? Names { get; set; }

    /// <summary>MusicBrainz artist ids (gid), when the source of the plays gives them (Last.fm).</summary>
    public List<string>? Mbids { get; set; }
}

public sealed record ResolvedGenre(int Id, string Name, int Votes);

public sealed record ResolvedArtist(
    string Input,
    string MatchType,
    int? ArtistId,
    string? Mbid,
    string? Name,
    IReadOnlyList<ResolvedGenre> Genres,
    // Spotify id linked to the artist in MusicBrainz (url relationships): link to Spotify even
    // when the plays come from the export, which has no artist ids
    string? SpotifyArtistId = null);

public sealed record ResolveArtistsResponse(
    IReadOnlyList<ResolvedArtist> BySpotifyId,
    IReadOnlyList<ResolvedArtist> ByName,
    IReadOnlyList<ResolvedArtist>? ByMbid = null);

public sealed record GenreNode(int Id, string Gid, string Name);

public sealed record GenreEdge(int FromGenreId, int ToGenreId, string Type);

/// <summary>
/// Whole genre graph. Edges read as "From {Type} To": "subgenre" (From is the parent),
/// "influenced by", "fusion of".
/// </summary>
public sealed record GenreGraph(IReadOnlyList<GenreNode> Genres, IReadOnlyList<GenreEdge> Relations);
