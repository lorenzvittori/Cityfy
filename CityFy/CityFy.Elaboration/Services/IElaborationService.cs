using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Services;

public interface IElaborationService
{
    /// <summary>
    /// Processa i TagGraph (tutti o filtrati per seedTag) e produce Graph salvati in mongo.
    /// </summary>
    Task<IEnumerable<Graph>> ProcessAsync(string? seedTag = null, CancellationToken cancellationToken = default);
}
