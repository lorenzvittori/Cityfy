using CityFy.CityGenerator.Models;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Mappers;

public sealed class GeneratedCityMapper : IGeneratedCityMapper
{
    public GeneratedCityInfo ToInfo(GeneratedCity c) => new(
        c.CityId, c.Name, c.From, c.To, c.Layout, c.Recipe, c.Genres, c.Subgenres,
        c.Status.ToString(), c.ErrorCode, c.CreatedAt, c.CompletedAt, c.CatalogVersion,
        c.Districts, c.Artists, c.TopGenres);

    public GeneratedCityDetail ToDetail(GeneratedCity c) =>
        c.Status == ProcessingStatus.Done ? new(ToInfo(c), c.City, c.Map) : new(ToInfo(c), null, null);
}
