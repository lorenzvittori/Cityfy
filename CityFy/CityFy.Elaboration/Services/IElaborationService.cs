using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Services;

public interface IElaborationService
{
    /// <summary>
    /// <summary>
    /// Processa i TagGraph forniti e produce Graph salvati in mongo.
    /// </summary>
    Task<IEnumerable<Graph>> ProcessAsync(IEnumerable<TagGraph> tagGraphs, CancellationToken cancellationToken = default);

    /// <summary>
    /// VECCHIO: obsoleto. Il servizio di elaborazione non deve recuperare collection di altri progetti.
    /// Usare il metodo che accetta i TagGraph come input.
    /// </summary>
    Task<IEnumerable<Graph>> ProcessAsync(string? seedTag = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera i TagGraph da un servizio remoto tramite client HTTP e processa i batch.
    /// baseUrl: es. https://localhost:5001
    /// </summary>
    Task<IEnumerable<Graph>> ProcessFromRemoteAsync(string baseUrl, int pageSize = 50, CancellationToken cancellationToken = default);
}
