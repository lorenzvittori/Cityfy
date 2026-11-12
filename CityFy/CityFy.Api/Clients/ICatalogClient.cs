using ServiceDefault.City;

namespace CityFy.Api.Clients;

/// <summary>Catalog: genres of the user, plus every play and the artist catalog of a user on data deletion.</summary>
public interface ICatalogClient : IUserDataOwner
{
    /// <summary>Genres and subgenres listened in [from, to). Throws CITY_INVALID_RANGE (400), CITY_CATALOG_MISSING (404).</summary>
    Task<IReadOnlyList<GenreOption>> GetGenresAsync(string userId, DateTime? from, DateTime? to, CancellationToken ct);
}
