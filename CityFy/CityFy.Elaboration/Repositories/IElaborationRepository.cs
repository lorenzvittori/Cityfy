using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Repositories;

public interface IElaborationRepository
{
    /// <summary>
    /// Recupera un Graph di output (elaborazione) per id dalla collection di elaborazione.
    /// </summary>
    Task<Graph?> GetGraphByIdAsync(string id);

    /// <summary>
    /// Recupera i Graph di output (elaborazione) paginati.
    /// </summary>
    Task<IEnumerable<Graph>> GetGraphsPagedAsync(int page, int pageSize);

    /// <summary>
    /// Inserisce il Graph nella collection di output (elaborazione).
    /// </summary>
    Task InsertGraphAsync(Graph graph);
}
