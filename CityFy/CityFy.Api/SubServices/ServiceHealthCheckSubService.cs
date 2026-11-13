using System.Diagnostics;
using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

public sealed class ServiceHealthCheckSubService(IHttpClientFactory factory) : IServiceHealthCheckSubService
{
    public const string Self = "Api";

    /// <summary>AppHost resource names, also used as health-probe HttpClient names (<see cref="ClientName"/>).</summary>
    /// <summary>The services probed over HTTP (all but the Api itself, which answers in-process).</summary>
    public static readonly string[] Others =
        ["Catalog", "CityGenerator", "Orchestrator", "RetrieveDumpSpotify", "RetrieveLastFm", "RetrieveMusicBrainz"];

    // After Others: static fields are initialized in declaration order
    public static readonly string[] Services = [Self, .. Others];

    public static string ClientName(string service) => $"health-{service}";

    public async Task<IReadOnlyList<ServiceHealthDto>> CheckAllAsync(CancellationToken ct)
    {
        var results = new List<ServiceHealthDto> { new(Self, "Healthy", 0, null) };
        results.AddRange(await Task.WhenAll(Others.Select(name => CheckAsync(name, ct))));
        return results;
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
