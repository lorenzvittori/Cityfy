using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Options;
using Microsoft.AspNetCore.DataProtection;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery, ProblemDetails
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("CityFy");
builder.Services.AddCityFyCors(builder.Configuration);
builder.Services.AddCityFyAuth(builder.Environment);

// Encrypts the session cookie. In a container the key ring must live on a persistent volume
// (DataProtection:KeysPath), otherwise every deploy/restart logs everybody out.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("CityFy.Api");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddOptions<FrontendOptions>()
    .BindConfiguration(FrontendOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Internal services, resolved by Aspire service discovery (resource names of the AppHost)
static void AddService<TClient>(IServiceCollection services, string resource, TimeSpan? uploadTimeout = null) where TClient : class
{
    var http = services.AddHttpClient<TClient>(c =>
    {
        c.BaseAddress = new Uri($"https+http://{resource}");
        if (uploadTimeout.HasValue) c.Timeout = uploadTimeout.Value;
    });

    // Standard resilience (retries, 30s total timeout) only for ordinary calls
    if (!uploadTimeout.HasValue)
        http.AddStandardResilienceHandler();
}

AddService<ElaborationClient>(builder.Services, "Elaboration");
AddService<OrchestratorClient>(builder.Services, "Orchestrator");
AddService<SpotifyServiceClient>(builder.Services, "RetrieveSpotify");
AddService<MusicBrainzServiceClient>(builder.Services, "RetrieveMusicBrainz");
// Uploads: large bodies, no automatic retry, long timeout
AddService<DumpClient>(builder.Services, "RetrieveDumpSpotify", uploadTimeout: TimeSpan.FromMinutes(10));

// --- Admin panel (mock admin user, Development only: see AdminOptions) ---
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
foreach (var service in CityFy.Api.Controllers.AdminController.Services.Where(s => s != "Api"))
    builder.Services.AddHttpClient($"health-{service}", c =>
    {
        c.BaseAddress = new Uri($"https+http://{service}");
        c.Timeout = TimeSpan.FromSeconds(5);
    });
// Read-only database browser: Mongo (cityfy) and SQL Server (musicbrainz)
builder.AddCityFyMongo();
builder.AddSqlServerClient("musicbrainz");
builder.Services.AddScoped<CityFy.Api.Admin.DatabaseBrowser>();
// Data deletion (user "Cancella i miei dati" and admin)
builder.Services.AddScoped<CityFy.Api.Account.UserDataEraser>();

var app = builder.Build();

// ProblemDetails errors, Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
