using CityFy.Elaboration.Models;
using ServiceDefault.City;

namespace CityFy.Elaboration.SubServices;

/// <summary>Pure transformation: per-artist plays + artist genres + genre hierarchy -> city (no I/O).</summary>
public interface ICityBuildSubService
{
    CityModel Build(
        string userId,
        string period,
        DateTime? from,
        DateTime now,
        IReadOnlyList<ArtistPlays> plays,
        IReadOnlyDictionary<string, ResolvedArtist> resolvedByArtistName,
        GenreHierarchy hierarchy);
}
