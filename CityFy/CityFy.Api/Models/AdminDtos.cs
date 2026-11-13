using System.Text.Json.Nodes;
using ServiceDefault.Models;

namespace CityFy.Api.Models;

/// <summary>Body of POST api/admin/lastfm/purge: Confirm must be exactly <see cref="Phrase"/>.</summary>
public sealed record LastFmPurgeRequest(string? Confirm)
{
    public const string Phrase = "DELETE ALL LASTFM DATA";
}

/// <summary>Accounts deleted on every service, and what Retrieve.LastFm removed.</summary>
public sealed record LastFmPurgeResult(int DeletedAccounts, UserDataDeletion LastFmService);

// Contract of the admin panel (api/admin). Downstream admin data is passed through as is.

public sealed record AdminLoginRequest(string Username, string Password);

public sealed record AdminUserDto(string? Username);

public sealed record ServiceHealthDto(string Name, string Status, long? ElapsedMs, string? Detail);

public sealed record AdminLinksDto(string? Dashboard, string? RabbitMq);

public sealed record ServicesOverviewDto(IReadOnlyList<ServiceHealthDto> Services, AdminLinksDto Links);

public sealed record TableInfo(string Name, long Rows);

public sealed record TablePage(string Name, long Total, int Page, int PageSize, IReadOnlyList<string> Columns, JsonArray Rows);

/// <summary>Result of a user's data deletion: what every service removed.</summary>
public sealed record UserDataDeletionResult(string UserId, IReadOnlyList<UserDataDeletion> Deleted);
