using CityFy.Catalog.Models;
using CityFy.Catalog.SubServices;
using ServiceDefault.City;

namespace CityFy.Catalog.Services;

public sealed class CityModelService(
    IDateRangeSubService ranges,
    IUserCatalogStoreSubService catalogs,
    IListeningAggregationSubService listening,
    ICityBuildSubService cityBuild,
    IGenreOptionsSubService genreOptions) : ICityModelService
{
    public async Task<CityModelResult> GetAsync(string userId, CityModelQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var (from, to) = ranges.Validate(query.From, query.To, now);
        var limit = cityBuild.Limit(query.MaxDistricts, query.ExtraGenres);

        var catalog = await catalogs.GetRequiredAsync(userId, ct);
        var placements = await catalogs.GetPlacementsAsync(catalog, ct);
        var plays = await listening.AggregateAsync(userId, from, to, ct);
        var city = cityBuild.Build(userId, from, to, now, plays, placements, new GenreFilter(query.Genres, query.Subgenres), limit);

        return new CityModelResult(city, catalog.Version);
    }

    public async Task<UserCatalogVersion> GetCatalogVersionAsync(string userId, CancellationToken ct)
    {
        var catalog = await catalogs.GetRequiredAsync(userId, ct);
        return new UserCatalogVersion(catalog.Version, catalog.UpdatedAt);
    }

    public async Task<IReadOnlyList<GenreOption>> GetGenresAsync(string userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (f, t) = ranges.Validate(from, to, DateTime.UtcNow);

        var catalog = await catalogs.GetRequiredAsync(userId, ct);
        var placements = await catalogs.GetPlacementsAsync(catalog, ct);
        var plays = await listening.AggregateAsync(userId, f, t, ct);

        return genreOptions.List(plays, placements);
    }
}
