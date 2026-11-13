using CityFy.Retrieve.MusicBrainz.Models;

namespace CityFy.Retrieve.MusicBrainz.SubServices;

public interface IGenreGraphSubService
{
    Task<GenreGraph> GetGraphAsync(CancellationToken ct);

    /// <summary>
    /// Top-level ancestors of a genre following "subgenre" edges upwards (a genre can have more
    /// than one parent). A genre without parents is its own root. Null if the genre does not exist.
    /// </summary>
    Task<IReadOnlyList<GenreNode>?> GetRootsAsync(int genreId, CancellationToken ct);
}