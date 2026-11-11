using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.EntityFrameworkCore;

namespace CityFy.Retrieve.MusicBrainz.Services;

public class MusicBrainzQueryService
{
    public const int MaxItemsPerRequest = 1000;
    private const string SpotifyArtistUriPrefix = "spotify:artist:";

    private readonly MusicBrainzContext _db;

    public MusicBrainzQueryService(MusicBrainzContext db)
    {
        _db = db;
    }

    public async Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct)
    {
        var spotifyInputs = Distinct(request.SpotifyArtistIds);
        var nameInputs = Distinct(request.Names);

        // Spotify id -> best artist id
        var spotifyIds = spotifyInputs.ToDictionary(i => i, NormalizeSpotifyId);
        var wantedSpotify = spotifyIds.Values.Distinct().ToList();
        var bySpotify = (await _db.ArtistSpotifyLinks
                .Where(l => wantedSpotify.Contains(l.SpotifyArtistId))
                .Select(l => new { l.SpotifyArtistId, l.ArtistId, l.Artist.TotalGenreVotes })
                .ToListAsync(ct))
            .GroupBy(x => x.SpotifyArtistId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TotalGenreVotes).First().ArtistId);

        // Name -> best artist id (SQL comparison is case-insensitive with the default collation)
        var byName = (await _db.Artists
                .Where(a => nameInputs.Contains(a.Name))
                .Select(a => new { a.Name, a.Id, a.TotalGenreVotes })
                .ToListAsync(ct))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TotalGenreVotes).First().Id, StringComparer.OrdinalIgnoreCase);

        var artistIds = bySpotify.Values.Concat(byName.Values).Distinct().ToList();
        var artists = await _db.Artists
            .Where(a => artistIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.Gid,
                a.Name,
                Genres = a.Genres.OrderByDescending(g => g.Votes)
                    .Select(g => new ResolvedGenre(g.GenreId, g.Genre.Name, g.Votes))
                    .ToList(),
                // an artist may have more than one Spotify page: take a stable one
                SpotifyId = a.SpotifyLinks.OrderBy(l => l.SpotifyArtistId).Select(l => l.SpotifyArtistId).FirstOrDefault()
            })
            .ToDictionaryAsync(a => a.Id, ct);

        ResolvedArtist Build(string input, int? artistId, string matchType)
        {
            if (artistId is { } id && artists.TryGetValue(id, out var a))
            {
                // matched by Spotify id: that is the right page; by name: the one linked in MusicBrainz
                var spotifyId = matchType == ArtistMatchTypes.SpotifyUrl ? spotifyIds[input] : a.SpotifyId;
                return new ResolvedArtist(input, matchType, a.Id, a.Gid, a.Name, a.Genres, spotifyId);
            }
            return new ResolvedArtist(input, ArtistMatchTypes.None, null, null, null, []);
        }

        return new ResolveArtistsResponse(
            spotifyInputs.Select(i => Build(i, bySpotify.TryGetValue(spotifyIds[i], out var id) ? id : null, ArtistMatchTypes.SpotifyUrl)).ToList(),
            nameInputs.Select(n => Build(n, byName.TryGetValue(n, out var id) ? id : null, ArtistMatchTypes.Name)).ToList());
    }

    public async Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct)
    {
        var genres = await _db.Genres.OrderBy(g => g.Name)
            .Select(g => new GenreNode(g.Id, g.Gid, g.Name)).ToListAsync(ct);
        var relations = await _db.GenreRelations
            .Select(r => new GenreEdge(r.FromGenreId, r.ToGenreId, r.RelationType)).ToListAsync(ct);
        return new GenreGraph(genres, relations);
    }

    /// <summary>
    /// Top-level ancestors of a genre following "subgenre" edges upwards (a genre can have more
    /// than one parent). A genre without parents is its own root. Null if the genre does not exist.
    /// </summary>
    public async Task<IReadOnlyList<GenreNode>?> GetGenreRootsAsync(int genreId, CancellationToken ct)
    {
        if (!await _db.Genres.AnyAsync(g => g.Id == genreId, ct))
            return null;

        var parentsOf = (await _db.GenreRelations
                .Where(r => r.RelationType == GenreRelationTypes.Subgenre)
                .Select(r => new { Parent = r.FromGenreId, Child = r.ToGenreId })
                .ToListAsync(ct))
            .ToLookup(r => r.Child, r => r.Parent);

        var roots = new HashSet<int>();
        var visited = new HashSet<int> { genreId };
        var queue = new Queue<int>([genreId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var parents = parentsOf[current].ToList();
            if (parents.Count == 0) roots.Add(current);
            // visited guards against cycles in community-edited data
            foreach (var p in parents.Where(visited.Add)) queue.Enqueue(p);
        }

        return await _db.Genres.Where(g => roots.Contains(g.Id)).OrderBy(g => g.Name)
            .Select(g => new GenreNode(g.Id, g.Gid, g.Name)).ToListAsync(ct);
    }

    private static List<string> Distinct(List<string>? values) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string NormalizeSpotifyId(string value) =>
        value.StartsWith(SpotifyArtistUriPrefix, StringComparison.OrdinalIgnoreCase) ? value[SpotifyArtistUriPrefix.Length..] : value;
}
