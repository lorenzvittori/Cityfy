namespace ServiceDefault.City;

// Contract of the CityGenerator API (api/city-generator/...), shared with the gateway.

/// <summary>
/// A city to generate. Name: optional (null = the UI composes one from the parameters).
/// From/To: date range, To exclusive (null = open). Layout: "organic" | "grid" | "custom"
/// (Recipe only with "custom"). Genres / Subgenres: see <see cref="CityModelQuery"/>.
/// MaxDistricts (0 = all, null = default) / ExtraGenres ("merge" | "drop", null = merge): see <see cref="DistrictLimit"/>.
/// </summary>
public sealed record GenerateCityRequest(
    string? Name = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Layout = null,
    string? Recipe = null,
    IReadOnlyList<int>? Genres = null,
    IReadOnlyList<int>? Subgenres = null,
    int? MaxDistricts = null,
    string? ExtraGenres = null);

/// <summary>
/// A generated city without its content (lists). Status: a ProcessingStatus name (string, as the
/// clients read enums). TopGenres: names of the main districts, for a default title.
/// MaxDistricts / ExtraGenres: null for the cities saved before they could be chosen (= the
/// default of <see cref="CityFormOptions"/>, merge).
/// </summary>
public sealed record GeneratedCityInfo(
    string CityId,
    string? Name,
    DateTime? From,
    DateTime? To,
    string Layout,
    string? Recipe,
    IReadOnlyList<int> Genres,
    IReadOnlyList<int> Subgenres,
    string Status,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? CatalogVersion,
    int Districts,
    int Artists,
    IReadOnlyList<string> TopGenres,
    int? MaxDistricts,
    string? ExtraGenres);

/// <summary>
/// Defaults and limits of the city form. DefaultDistricts: districts when the request does not
/// choose; WarnDistricts: beyond it the map gets crowded (CITY_MANY_DISTRICTS), a warning only.
/// </summary>
public sealed record CityFormOptions(int DefaultDistricts, int WarnDistricts, int MaxNameLength, int MaxGenres);

/// <summary>City (data) and Map (geometry): null until the generation is Done.</summary>
public sealed record GeneratedCityDetail(GeneratedCityInfo Info, CityModel? City, CityLayout? Map);

public sealed record GeneratedCityPage(IReadOnlyList<GeneratedCityInfo> Items, int Page, int PageSize, long Total);

/// <summary>Current artist catalog of a user (Catalog api/catalog/version/{userId}).</summary>
public sealed record UserCatalogVersion(string Version, DateTime UpdatedAt);
