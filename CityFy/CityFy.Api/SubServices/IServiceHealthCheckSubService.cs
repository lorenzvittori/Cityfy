using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

public interface IServiceHealthCheckSubService
{
    /// <summary>/health of every service, in parallel (the Api itself answers in-process).</summary>
    Task<IReadOnlyList<ServiceHealthDto>> CheckAllAsync(CancellationToken ct);
}
