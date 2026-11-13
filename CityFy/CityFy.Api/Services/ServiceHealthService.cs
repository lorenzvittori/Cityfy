using CityFy.Api.Models;
using CityFy.Api.Options;
using CityFy.Api.SubServices;
using Microsoft.Extensions.Options;

namespace CityFy.Api.Services;

public sealed class ServiceHealthService(IServiceHealthCheckSubService checks, IOptions<AdminOptions> options) : IServiceHealthService
{
    public async Task<ServicesOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var services = await checks.CheckAllAsync(ct);
        var o = options.Value;
        return new ServicesOverviewDto(services, new AdminLinksDto(o.DashboardUrl, o.RabbitMqManagementUrl));
    }
}
