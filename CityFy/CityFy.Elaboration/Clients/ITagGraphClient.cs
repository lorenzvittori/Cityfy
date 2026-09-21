using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Clients;

public interface ITagGraphClient
{
    /// <summary>
    /// Recupera i TagGraph da un servizio retrieve paginato.
    /// </summary>
    Task<IEnumerable<TagGraph>> GetTagGraphsPagedAsync(string baseUrl, int page, int pageSize, CancellationToken cancellationToken = default);
}
