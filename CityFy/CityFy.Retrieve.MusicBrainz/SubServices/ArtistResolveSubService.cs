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
        if ((request.SpotifyArtistIds?.Count ?? 0) > MaxItemsPerRequest || (request.Names?.Count ?? 0) > MaxItemsPerRequest)
            throw new CityFyException(ErrorCodes.MbTooManyItems,
                $"At most {MaxItemsPerRequest} spotifyArtistIds and {MaxItemsPerRequest} names per request", StatusCodes.Status400BadRequest);

        var spotifyInputs = Distinct(request.SpotifyArtistIds);
        var nameInputs = Distinct(request.Names);

        // Spotify id -> best artist id
        var spotifyIds = spotifyInputs.ToDictionary(i => i, NormalizeSpotifyId);
        var wantedSpotify = spotifyIds.Values.Distinct().ToList();
        var bySpotify = (await db.ArtistSpotifyLinks
                .Where(l => wantedSpotify.Contains(l.SpotifyArtistId))
                .Select(l => new { l.SpotifyArtistId, l.ArtistId, l.Artist.TotalGenreVotes })
                .ToListAsync(ct))
            .GroupBy(x => x.SpotifyArtistId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TotalGenreVotes).First().ArtistId);

        // Name -> best artist id (SQL comparison is case-insensitive with the default collation)
        var byName = (await db.Artists
                .Where(a => nameInputs.Contains(a.Name))
                .Select(a => new { a.Name, a.Id, a.TotalGenreVotes })
                .ToListAsync(ct))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.TotalGenreVotes).First().Id, StringComparer.OrdinalIgnoreCase);

        var artistIds = bySpotify.Values.Concat(byName.Values).Distinct().ToList();
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

        return new ResolveArtistsResponse(
            spotifyInputs.Select(i => Build(i, bySpotify.TryGetValue(spotifyIds[i], out var id) ? id : null, ArtistMatchTypes.SpotifyUrl)).ToList(),
            nameInputs.Select(n => Build(n, byName.TryGetValue(n, out var id) ? id : null, ArtistMatchTypes.Name)).ToList());
    }

    private static List<string> Distinct(List<string>? values) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string NormalizeSpotifyId(string value) =>
        value.StartsWith(SpotifyArtistUriPrefix, StringComparison.OrdinalIgnoreCase) ? value[SpotifyArtistUriPrefix.Length..] : value;
}
