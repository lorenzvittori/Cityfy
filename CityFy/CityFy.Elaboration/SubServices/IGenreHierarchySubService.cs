using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.SubServices;

public interface IGenreHierarchySubService
{
    /// <summary>Subgenre hierarchy of the MusicBrainz genre graph (cached for an hour).</summary>
    Task<GenreHierarchy> GetAsync(CancellationToken ct);
}
