using ServiceDefault.City;
using CityFy.Elaboration.Clients;
using CityFy.Elaboration.Models;
using CityFy.Elaboration.Options;
using CityFy.Elaboration.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CityFy.Elaboration.Services;

public sealed class CityService(
    IElaborationRepository repo,
    IMusicBrainzClient musicBrainz,
    IMemoryCache cache,
    IOptions<CityOptions> options,
    ILogger<CityService> logger) : ICityService
{
    private const string SpotifyArtistUriPrefix = "spotify:artist:";
    private const string NoMatch = "none";
    private static readonly TimeSpan GenreGraphCacheDuration = TimeSpan.FromHours(1);

    private readonly CityOptions _options = options.Value;

    public async Task<CityModel> BuildAsync(string userId, string period, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var from = Periods.GetStart(period, now);

        var plays = await repo.AggregateArtistPlaysAsync(userId, from, _options.QualifiedPlayMs, ct);
        var resolved = await ResolveAsync(plays, ct);
        var hierarchy = await GetHierarchyAsync(ct);

        var city = new CityBuilder(_options).Build(userId, period, from, now, plays, resolved, hierarchy);

        // A user without any play (e.g. data deleted while a build was queued) gets no city: storing
        // one would bring back data the user asked to delete. Checked right before saving.
        if (plays.Count == 0 && !await repo.HasPlaysAsync(userId, ct))
        {
            logger.LogInformation("City {CityId} not stored: user {UserId} has no plays", city.Id, userId);
            return city;
        }

        await repo.SaveCityAsync(city, ct);

        logger.LogInformation(
            "City {CityId}: {Districts} districts, {Placed}/{Artists} artists placed, coverage {Coverage}% of qualified plays",
            city.Id, city.Districts.Count, city.Summary.PlacedArtists, city.Summary.TotalArtists, city.Summary.CoveragePercent);

        return city;
    }

    public Task<CityModel?> GetAsync(string userId, string period, CancellationToken ct) =>
        repo.GetCityAsync(userId, period, ct);

    // Spotify id match first (reliable), name match as fallback (export plays have no artist id)
    private async Task<Dictionary<string, ResolvedArtist>> ResolveAsync(IReadOnlyList<ArtistPlays> plays, CancellationToken ct)
    {
        var request = new ResolveArtistsRequest
        {
            SpotifyArtistIds = plays.Select(p => SpotifyId(p.ArtistUri)).OfType<string>().Distinct().ToList(),
            Names = plays.Select(p => p.ArtistName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        var response = await musicBrainz.ResolveArtistsAsync(request, ct);
        var bySpotify = response.BySpotifyId.Where(r => r.MatchType != NoMatch)
            .ToDictionary(r => r.Input, StringComparer.Ordinal);
        var byName = response.ByName.Where(r => r.MatchType != NoMatch)
            .GroupBy(r => r.Input, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ResolvedArtist>();
        foreach (var p in plays)
        {
            if (SpotifyId(p.ArtistUri) is { } sid && bySpotify.TryGetValue(sid, out var s))
                result[p.ArtistName] = s;
            else if (byName.TryGetValue(p.ArtistName.Trim(), out var n)) // the resolve API trims its inputs
                result[p.ArtistName] = n;
        }
        return result;
    }

    private async Task<GenreHierarchy> GetHierarchyAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync("musicbrainz:genre-hierarchy", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = GenreGraphCacheDuration;
            return new GenreHierarchy(await musicBrainz.GetGenreGraphAsync(ct));
        }))!;

    private static string? SpotifyId(string? artistUri) =>
        artistUri != null && artistUri.StartsWith(SpotifyArtistUriPrefix, StringComparison.Ordinal)
            ? artistUri[SpotifyArtistUriPrefix.Length..]
            : null;
}
