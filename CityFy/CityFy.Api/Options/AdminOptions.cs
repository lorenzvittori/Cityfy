namespace CityFy.Api.Options;

/// <summary>
/// "Admin" section: the admin panel with a MOCK user defined here (no identity system yet).
/// Disabled by default; appsettings.Development.json enables it for local development.
/// The panel can read every table and trigger imports: never enable it on a public deployment.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public bool Enabled { get; set; }

    public string Username { get; set; } = "admin";

    public string Password { get; set; } = "cityfy-admin";

    /// <summary>Aspire dashboard (structured logs, traces): link shown in the panel.</summary>
    public string? DashboardUrl { get; set; }

    /// <summary>RabbitMQ management UI (queues, DLQ): link shown in the panel.</summary>
    public string? RabbitMqManagementUrl { get; set; }
}
