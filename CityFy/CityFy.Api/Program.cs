using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Consumers;
using CityFy.Api.HostedServices;
using CityFy.Api.Options;
using CityFy.Api.RateLimiting;
using CityFy.Api.Repositories;
using CityFy.Api.Services;
using CityFy.Api.SubServices;
using Microsoft.AspNetCore.DataProtection;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery, ProblemDetails
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("CityFy");
builder.Services.AddCityFyCors(builder.Configuration);
builder.Services.AddCityFyAuth(builder.Environment);
// Limits per client network on the public API (sessions, uploads, Last.fm login, everything else)
builder.Services.AddCityFyRateLimits();

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
static void AddService<TClient, TImplementation>(IServiceCollection services, string resource, TimeSpan? uploadTimeout = null)
    where TClient : class
    where TImplementation : class, TClient
{
    var http = services.AddHttpClient<TClient, TImplementation>(c =>
    {
        c.BaseAddress = new Uri($"https+http://{resource}");
        if (uploadTimeout.HasValue) c.Timeout = uploadTimeout.Value;
    });

    // Standard resilience (retries, 30s total timeout) only for ordinary calls
    if (!uploadTimeout.HasValue)
        http.AddStandardResilienceHandler();
}

AddService<ICatalogClient, CatalogClient>(builder.Services, "Catalog");
AddService<ICityGeneratorClient, CityGeneratorClient>(builder.Services, "CityGenerator");
AddService<IOrchestratorClient, OrchestratorClient>(builder.Services, "Orchestrator");
AddService<ILastFmServiceClient, LastFmServiceClient>(builder.Services, "RetrieveLastFm");
AddService<IMusicBrainzServiceClient, MusicBrainzServiceClient>(builder.Services, "RetrieveMusicBrainz");
// Uploads: large bodies, no automatic retry, long timeout
AddService<IDumpClient, DumpClient>(builder.Services, "RetrieveDumpSpotify", uploadTimeout: TimeSpan.FromMinutes(10));

// Data deletion (user "delete my data" and admin)
builder.Services.AddScoped<IUserDataEraser, UserDataEraser>();
builder.Services.AddScoped<IUserDataDeletionSubService, UserDataDeletionSubService>();

// Anonymous sessions ('anonymous_sessions'): validity checked on every request by the cookie events,
// data deleted on every service when they expire
builder.Services.AddOptions<AnonymousSessionOptions>()
    .BindConfiguration(AnonymousSessionOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IAnonymousSessionRepository, MongoAnonymousSessionRepository>();
builder.Services.AddScoped<IAnonymousSessionStoreSubService, AnonymousSessionStoreSubService>();
builder.Services.AddScoped<IAnonymousSessionService, AnonymousSessionService>();
builder.Services.AddHostedService<ExpiredSessionCleanupScheduler>();

// CityFy accounts ('users'): login with Last.fm, move of the data of an anonymous session
builder.Services.AddSingleton<IUserRepository, MongoUserRepository>();
builder.Services.AddScoped<IUserStoreSubService, UserStoreSubService>();
builder.Services.AddScoped<IUserDataTransferSubService, UserDataTransferSubService>();
builder.Services.AddScoped<IAccountService, AccountService>();
// End of the Last.fm licence (admin): every Last.fm account and its data
builder.Services.AddScoped<ILastFmPurgeService, LastFmPurgeService>();

// Live events (api/v1/live, SSE): NowPlayingChanged from Retrieve.LastFm to the open pages, UserActive back
builder.AddCityFyMessaging();
builder.Services.AddSingleton<ILiveHubSubService, LiveHubSubService>();
builder.Services.AddSingleton<IUserActivityEventSubService, UserActivityEventSubService>();
builder.Services.AddScoped<ILiveService, LiveService>();
builder.Services.AddMessageConsumer<NowPlayingChangedConsumer>();

// --- Admin panel (mock admin user, Development only: see AdminOptions) ---
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
foreach (var service in ServiceHealthCheckSubService.Services.Where(s => s != ServiceHealthCheckSubService.Self))
    builder.Services.AddHttpClient(ServiceHealthCheckSubService.ClientName(service), c =>
    {
        c.BaseAddress = new Uri($"https+http://{service}");
        c.Timeout = TimeSpan.FromSeconds(5);
    });
builder.Services.AddScoped<IServiceHealthService, ServiceHealthService>();
builder.Services.AddSingleton<IServiceHealthCheckSubService, ServiceHealthCheckSubService>();
// Mongo (cityfy): anonymous sessions, plus the read-only database browser with SQL Server (musicbrainz)
builder.AddCityFyMongo();
builder.AddSqlServerClient("musicbrainz");
builder.Services.AddScoped<IDatabaseBrowser, DatabaseBrowser>();
builder.Services.AddSingleton<IMongoBrowseSubService, MongoBrowseSubService>();
builder.Services.AddScoped<ISqlBrowseSubService, SqlBrowseSubService>();
builder.Services.AddSingleton<IRedactionSubService, RedactionSubService>();

var app = builder.Build();

// Client address from the reverse proxy (X-Forwarded-For, trusted proxies only): first, everything else counts on it
app.UseForwardedHeaders();
// ProblemDetails errors, Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthentication();
// After the authentication: the uploads are also counted per user
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.Run();
