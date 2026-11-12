using System.Diagnostics;
using CityFy.Api.Models;
using CityFy.Api.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Api.Services;

public sealed class ServiceHealthService(IHttpClientFactory factory, IOptions<AdminOptions> options) : IServiceHealthService
{
    public const string Self = "Api";

    /// <summary>AppHost resource names, also used as health-probe HttpClient names (<see cref="ClientName"/>).</summary>
    public static readonly string[] Services =
        [Self, "Elaboration", "Orchestrator", "RetrieveSpotify", "RetrieveDumpSpotify", "RetrieveMusicBrainz"];

    public static string ClientName(string service) => $"health-{service}";

    public async Task<ServicesOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var checks = Services.Where(s => s != Self).Select(name => CheckAsync(name, ct));

        var results = new List<ServiceHealthDto> { new(Self, "Healthy", 0, null) };
        results.AddRange(await Task.WhenAll(checks));

        var o = options.Value;
        return new ServicesOverviewDto(results, new AdminLinksDto(o.DashboardUrl, o.RabbitMqManagementUrl));
    }

    private async Task<ServiceHealthDto> CheckAsync(string name, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await factory.CreateClient(ClientName(name)).GetAsync("health", ct);
            return new ServiceHealthDto(name, resp.IsSuccessStatusCode ? "Healthy" : "Unhealthy", sw.ElapsedMilliseconds,
                resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ServiceHealthDto(name, "Unreachable", null, ex.Message);
        }
    }
}
