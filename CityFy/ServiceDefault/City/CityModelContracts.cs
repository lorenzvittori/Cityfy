namespace ServiceDefault.City;

// Contract of the Catalog city-model API (api/catalog/city-model, api/catalog/genres), shared by
// its callers (city generator, gateway).

/// <summary>
/// City of a date range, only with the chosen genres. From/To null = from the first play / up to
/// now; To is exclusive. Genres = district (top-level genre) ids, Subgenres = block (genre) ids:
/// an artist is kept when its district is in Genres or its block is in Subgenres; both empty =
/// every genre. Artists left out disappear from the city (they are not counted anywhere).
/// </summary>
public sealed record CityModelQuery(
    DateTime? From = null,
    DateTime? To = null,
    IReadOnlyList<int>? Genres = null,
    IReadOnlyList<int>? Subgenres = null);

/// <summary>CatalogVersion: version of the artist catalog the city was computed with.</summary>
public sealed record CityModelResult(CityModel City, string CatalogVersion);

/// <summary>A district (top-level genre) that can be chosen for a city, with its subgenres (blocks).</summary>
public sealed record GenreOption(int GenreId, string Name, string Color, long QualifiedPlays, int Artists, IReadOnlyList<SubgenreOption> Subgenres);

public sealed record SubgenreOption(int GenreId, string Name, long QualifiedPlays, int Artists);
