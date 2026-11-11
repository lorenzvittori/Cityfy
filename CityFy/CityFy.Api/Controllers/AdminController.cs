using CityFy.Api.Admin;
using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Api.Controllers;

/// <summary>Admin endpoints answer 404 when the panel is disabled (Admin:Enabled=false), as if they did not exist.</summary>
public sealed class AdminEnabledAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AdminOptions>>().Value.Enabled)
            context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}

[ApiController]
[AdminEnabled]
[ApiExplorerSettings(GroupName = "admin")]
[Route("api/admin")]
public class AdminAuthController : ControllerBase
{
    public sealed record LoginRequest(string Username, string Password);

    // POST api/admin/login — mock admin user from configuration (Admin:Username / Admin:Password)
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, [FromServices] IOptions<AdminOptions> options)
    {
        var o = options.Value;
        if (!FixedTimeEquals(request.Username, o.Username) | !FixedTimeEquals(request.Password, o.Password))
            return this.CityFyProblem(ErrorCodes.AdminInvalidCredentials, "Invalid admin credentials", StatusCodes.Status401Unauthorized);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthSetup.CreateAdminPrincipal(o.Username));
        return Ok(new { username = o.Username });
    }

    // GET api/admin/me — is the current session an admin?
    [Authorize(Policy = AuthSetup.AdminPolicy)]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new { username = User.Identity?.Name });

    // POST api/admin/logout
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private static bool FixedTimeEquals(string? a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a ?? ""), System.Text.Encoding.UTF8.GetBytes(b));
}

/// <summary>Operations on the services: health, MusicBrainz import, pipeline, Spotify, uploads, databases.</summary>
[ApiController]
[AdminEnabled]
[Authorize(Policy = AuthSetup.AdminPolicy)]
[ApiExplorerSettings(GroupName = "admin")]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    /// <summary>AppHost resource names, also used as health-probe HttpClient names.</summary>
    public static readonly string[] Services =
        ["Api", "Elaboration", "Orchestrator", "RetrieveSpotify", "RetrieveDumpSpotify", "RetrieveMusicBrainz"];

    public sealed record ServiceHealth(string Name, string Status, long? ElapsedMs, string? Detail);

    // GET api/admin/services — /health of every service (the Api itself answers in-process)
    [HttpGet("services")]
    public async Task<IActionResult> GetServices([FromServices] IHttpClientFactory factory, [FromServices] IOptions<AdminOptions> options, CancellationToken ct)
    {
        var checks = Services.Where(s => s != "Api").Select(async name =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var resp = await factory.CreateClient($"health-{name}").GetAsync("health", ct);
                return new ServiceHealth(name, resp.IsSuccessStatusCode ? "Healthy" : "Unhealthy", sw.ElapsedMilliseconds,
                    resp.IsSuccessStatusCode ? null : $"HTTP {(int)resp.StatusCode}");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return new ServiceHealth(name, "Unreachable", null, ex.Message);
            }
        });

        var results = new List<ServiceHealth> { new("Api", "Healthy", 0, null) };
        results.AddRange(await Task.WhenAll(checks));

        var o = options.Value;
        return Ok(new { services = results, links = new { dashboard = o.DashboardUrl, rabbitMq = o.RabbitMqManagementUrl } });
    }

    // --- MusicBrainz catalog ---------------------------------------------------------

    [HttpGet("musicbrainz/import")]
    public async Task<IActionResult> GetImport([FromServices] MusicBrainzServiceClient mb, CancellationToken ct) =>
        Ok(await mb.GetImportStatusAsync(ct));

    [HttpPost("musicbrainz/import")]
    public async Task<IActionResult> StartImport([FromServices] MusicBrainzServiceClient mb, CancellationToken ct) =>
        Accepted(await mb.StartImportAsync(ct));

    // --- Pipeline (orchestrator) -------------------------------------------------------

    [HttpGet("pipeline/runs")]
    public async Task<IActionResult> GetRuns([FromServices] OrchestratorClient orchestrator, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await orchestrator.ListAllRunsAsync(Math.Clamp(limit, 1, 200), ct));

    [HttpPost("pipeline/rebuild/{userId}")]
    public async Task<IActionResult> Rebuild(string userId, [FromServices] OrchestratorClient orchestrator, CancellationToken ct) =>
        Accepted(await orchestrator.TriggerAsync(userId, ct));

    // --- Spotify accounts and uploads --------------------------------------------------

    [HttpGet("spotify/accounts")]
    public async Task<IActionResult> GetAccounts([FromServices] SpotifyServiceClient spotify, CancellationToken ct) =>
        Ok(await spotify.ListAccountsAsync(ct));

    [HttpGet("spotify/jobs")]
    public async Task<IActionResult> GetJobs([FromServices] SpotifyServiceClient spotify, [FromQuery] int limit = 30, CancellationToken ct = default) =>
        Ok(await spotify.ListJobsAsync(Math.Clamp(limit, 1, 200), ct));

    [HttpPost("spotify/accounts/{userId}/sync")]
    public async Task<IActionResult> Sync(string userId, [FromServices] SpotifyServiceClient spotify, CancellationToken ct) =>
        Accepted(await spotify.StartRetrieveAsync(userId, ct));

    [HttpGet("uploads")]
    public async Task<IActionResult> GetUploads([FromServices] DumpClient dump, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await dump.ListUploadsAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, 200), ct));

    // --- Cancellation -----------------------------------------------------------------
    // 404 when the id does not exist, 409 PROCESS_NOT_CANCELLABLE when it already finished.

    [HttpPost("musicbrainz/import/cancel")]
    public async Task<IActionResult> CancelImport([FromServices] MusicBrainzServiceClient mb, CancellationToken ct) =>
        Accepted(await mb.CancelImportAsync(ct));

    [HttpPost("uploads/{uploadId}/cancel")]
    public async Task<IActionResult> CancelUpload(string uploadId, [FromServices] DumpClient dump, CancellationToken ct) =>
        Accepted(await dump.CancelUploadAsync(uploadId, ct));

    [HttpPost("spotify/jobs/{jobId}/cancel")]
    public async Task<IActionResult> CancelSpotifyJob(string jobId, [FromServices] SpotifyServiceClient spotify, CancellationToken ct) =>
        Accepted(await spotify.CancelJobAsync(jobId, ct));

    [HttpPost("pipeline/runs/{runId}/cancel")]
    public async Task<IActionResult> CancelRun(string runId, [FromServices] OrchestratorClient orchestrator, CancellationToken ct) =>
        Accepted(await orchestrator.CancelRunAsync(runId, ct));

    // --- Databases (read-only) ---------------------------------------------------------

    [HttpGet("db/mongo")]
    public async Task<IActionResult> GetCollections([FromServices] DatabaseBrowser db, CancellationToken ct) =>
        Ok(await db.ListCollectionsAsync(ct));

    [HttpGet("db/mongo/{name}")]
    public async Task<IActionResult> GetCollection(string name, [FromServices] DatabaseBrowser db,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await db.GetCollectionPageAsync(name, page, pageSize, ct));

    [HttpGet("db/sql")]
    public async Task<IActionResult> GetTables([FromServices] DatabaseBrowser db, CancellationToken ct) =>
        Ok(await db.ListTablesAsync(ct));

    [HttpGet("db/sql/{name}")]
    public async Task<IActionResult> GetTable(string name, [FromServices] DatabaseBrowser db,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await db.GetTablePageAsync(name, page, pageSize, ct));
}
