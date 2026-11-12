using CityFy.Api.Models;

namespace CityFy.Api.Services;

public interface IServiceHealthService
{
    /// <summary>/health of every service (the Api itself answers in-process) and links to the monitoring tools.</summary>
    Task<ServicesOverviewDto> GetOverviewAsync(CancellationToken ct);
}
