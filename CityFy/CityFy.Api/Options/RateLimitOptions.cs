using System.ComponentModel.DataAnnotations;

namespace CityFy.Api.Options;

/// <summary>
/// "RateLimits" section: limits per client network (IPv4 address or IPv6 /64) on the public API.
/// Internal and admin endpoints are not limited. A limit reached answers 429 RATE_LIMITED + Retry-After.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>"Inizia senza account" per network per hour: enough for a family or an office behind one IP.</summary>
    [Range(1, 100_000)]
    public int SessionStartsPerHour { get; set; } = 10;

    /// <summary>Uploads of the export (up to 200 MB each) per network per hour.</summary>
    [Range(1, 100_000)]
    public int UploadsPerHour { get; set; } = 5;

    /// <summary>Uploads per user (session or account) per day.</summary>
    [Range(1, 100_000)]
    public int UploadsPerUserPerDay { get; set; } = 10;

    /// <summary>Uploads in progress at the same time on the whole server (disk and CPU of one machine).</summary>
    [Range(1, 1000)]
    public int ConcurrentUploads { get; set; } = 3;

    /// <summary>Last.fm logins (login + callback) per network per hour: each one calls Last.fm, a new account imports its history.</summary>
    [Range(1, 100_000)]
    public int LastFmLoginsPerHour { get; set; } = 20;

    /// <summary>Any other request of api/v1 per network per minute: a wide ceiling against scripts.</summary>
    [Range(1, 1_000_000)]
    public int ApiRequestsPerMinute { get; set; } = 300;
}

/// <summary>
/// "Proxy" section: networks of the reverse proxies whose X-Forwarded-For is trusted (Caddy in
/// production). Without it every request would come from the proxy and share one rate-limit counter.
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>CIDR, e.g. "172.16.0.0/12" (Docker networks). Loopback is always trusted.</summary>
    public List<string> KnownNetworks { get; set; } = [];
}
