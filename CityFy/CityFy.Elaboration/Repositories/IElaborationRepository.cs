using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Repositories;

public interface IElaborationRepository
{
    /// <summary>
    /// Recupera i TagGraph dalla collection musicbrainz_tag_graphs. Se seedTag è specificato filtra.
    /// </summary>
    Task<IEnumerable<TagGraph>> GetTagGraphsAsync(string? seedTag = null);

    /// <summary>
    /// Inserisce il Graph nella collection di output.
    /// </summary>
    Task InsertGraphAsync(Graph graph);
}
