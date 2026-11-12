using CityFy.Catalog.Clients;
using CityFy.Catalog.Models;
using Microsoft.Extensions.Caching.Memory;

namespace CityFy.Catalog.SubServices;

public sealed class GenreHierarchySubService(IMusicBrainzClient musicBrainz, IMemoryCache cache) : IGenreHierarchySubService
{
    private const string CacheKey = "musicbrainz:genre-hierarchy";

    /// <summary>The catalog changes only with a new MusicBrainz import (hours/days).</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public async Task<GenreHierarchy> GetAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return new GenreHierarchy(await musicBrainz.GetGenreGraphAsync(ct));
        }))!;
}
