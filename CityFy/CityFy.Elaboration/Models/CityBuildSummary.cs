using ServiceDefault.City;

namespace CityFy.Elaboration.Models;

/// <summary>Result of a synchronous build on the debug endpoint.</summary>
public sealed record CityBuildSummary(
    string Id,
    DateTime GeneratedAt,
    CitySummary Summary,
    int Districts,
    IReadOnlyList<UnresolvedArtist> Unresolved);
