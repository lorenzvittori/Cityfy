using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Models;
using Microsoft.EntityFrameworkCore;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.MusicBrainz.SubServices;

public sealed class ArtistResolveSubService(MusicBrainzContext db) : IArtistResolveSubService
{
    /// <summary>Same limit used by the callers to split their requests in batches.</summary>
    public const int MaxItemsPerRequest = 1000;
    private const string SpotifyArtistUriPrefix = "spotify:artist:";
    public async Task<ResolveArtistsResponse> ResolveAsync(ResolveArtistsRequest request, CancellationToken ct)
    {
        if ((request.SpotifyArtistIds?.Count ?? 0) > MaxItemsPerRequest || (request.Names?.Count ?? 0) > MaxItemsPerRequest ||
            (request.Mbids?.Count ?? 0) > MaxItemsPerRequest)
            throw new CityFyException(ErrorCodes.MbTooManyItems,
                $"At most {MaxItemsPerRequest} spotifyArtistIds, {MaxItemsPerRequest} names and {MaxItemsPerRequest} mbids per request",
                StatusCodes.Status400BadRequest);

        var spotifyInputs = Distinct(request.SpotifyArtistIds);
        var nameInputs = Distinct(request.Names);
        var mbidInputs = Distinct(request.Mbids);

        // MusicBrainz id -> artist id (exact; gid stored in lower case)
        var wantedMbids = mbidInputs.Select(m => m.ToLowerInvariant()).ToList();
        var byMbid = (await db.Artists
                .Where(a => wantedMbids.Contains(a.Gid))
                .Select(a => new { a.Gid, a.Id })
                .ToListAsync(ct))
            .ToDictionary(x => x.Gid, x => x.Id, StringComparer.OrdinalIgnoreCase);

        // Spotify id -> best artist id
        var spotifyIds = spotifyInputs.ToDictionary(i => i, NormalizeSpotifyId);
        var wantedSpotify = spotifyIds.Values.Distinct().ToList();
        var bySpotify = BestByKey(await db.ArtistSpotifyLinks
                .Where(l => wantedSpotify.Contains(l.SpotifyArtistId))
                .Select(l => new Candidate(l.SpotifyArtistId, l.ArtistId, l.Artist.TotalGenreVotes))
                .ToListAsync(ct),
            StringComparer.Ordinal);

        // Name -> best artist id (SQL comparison is case-insensitive with the default collation)
        var byName = BestByKey(await db.Artists
                .Where(a => nameInputs.Contains(a.Name))
                .Select(a => new Candidate(a.Name, a.Id, a.TotalGenreVotes))
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        var artistIds = bySpotify.Values.Concat(byName.Values).Concat(byMbid.Values).Distinct().ToList();
        var artists = await db.Artists
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

        ResolvedArtist BySpotify(string input) => Build(input, IdOf(bySpotify, spotifyIds[input]), ArtistMatchTypes.SpotifyUrl);
        ResolvedArtist ByName(string input) => Build(input, IdOf(byName, input), ArtistMatchTypes.Name);
        ResolvedArtist ByMbid(string input) => Build(input, IdOf(byMbid, input), ArtistMatchTypes.Mbid);

        return new ResolveArtistsResponse(
            spotifyInputs.Select(BySpotify).ToList(),
            nameInputs.Select(ByName).ToList(),
            mbidInputs.Select(ByMbid).ToList());
    }

    /// <summary>An artist found for a key (Spotify id or name), with its genre votes to rank homonyms.</summary>
    private sealed record Candidate(string Key, int ArtistId, int Votes);

    /// <summary>Per key, the candidate with most votes (the first one on ties).</summary>
    private static Dictionary<string, int> BestByKey(IEnumerable<Candidate> candidates, StringComparer comparer)
    {
        var best = new Dictionary<string, Candidate>(comparer);
        foreach (var c in candidates)
            if (!best.TryGetValue(c.Key, out var current) || c.Votes > current.Votes)
                best[c.Key] = c;

        var ids = new Dictionary<string, int>(comparer);
        foreach (var (key, c) in best)
            ids[key] = c.ArtistId;
        return ids;
    }

    private static int? IdOf(IReadOnlyDictionary<string, int> ids, string key) =>
        ids.TryGetValue(key, out var id) ? id : null;

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Trimmed(string value) => value.Trim();

    private static List<string> Distinct(List<string>? values) =>
        (values ?? []).Where(HasText).Select(Trimmed)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string NormalizeSpotifyId(string value) =>
        value.StartsWith(SpotifyArtistUriPrefix, StringComparison.OrdinalIgnoreCase) ? value[SpotifyArtistUriPrefix.Length..] : value;
}
