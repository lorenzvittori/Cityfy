using CityFy.Retrieve.Spotify.Clients;
using CityFy.Retrieve.Spotify.HostedServices;
using CityFy.Retrieve.Spotify.Mappers;
using CityFy.Retrieve.Spotify.Options;
using CityFy.Retrieve.Spotify.Repositories;
using CityFy.Retrieve.Spotify.Services;
using Microsoft.AspNetCore.DataProtection;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Retrieve Spotify");
builder.Services.AddCityFyCors(builder.Configuration);

// "Spotify" section validated at startup (fails fast if e.g. ClientSecret is missing)
builder.Services.AddOptions<SpotifyOptions>()
    .BindConfiguration(SpotifyOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// OAuth state + access tokens (in memory)
builder.Services.AddMemoryCache();

// Encrypts the stored refresh tokens. The key ring must survive restarts, otherwise the
// stored tokens can no longer be decrypted and users must log in again: in a container set
// DataProtection:KeysPath to a persistent volume.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("CityFy.Retrieve.Spotify");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// Shared Mongo client/database + writer for 'listening_events'
builder.AddCityFyMongo();
builder.Services.AddListeningEventStore();

// Typed HttpClients via IHttpClientFactory (pooled handlers, no socket exhaustion).
// Typed clients are transient, so their consumers are scoped, not singletons.
builder.Services.AddHttpClient<ISpotifyClient, SpotifyClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<ISpotifyAuthClient, SpotifyAuthClient>(c => c.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddSingleton<ISpotifyConverter, SpotifyConverter>();
builder.Services.AddSingleton<ISpotifyRepository, MongoSpotifyRepository>();
builder.Services.AddSingleton<ISpotifyAccountRepository, MongoSpotifyAccountRepository>();
builder.Services.AddScoped<ISpotifyAccountService, SpotifyAccountService>();
builder.Services.AddScoped<ISpotifyAuthService, SpotifyAuthService>();
builder.Services.AddScoped<ISpotifyRetrieveService, SpotifyRetrieveService>();

// Background retrieve: controller/scheduler enqueue, hosted service executes
builder.Services.AddSingleton<IRetrieveJobQueue, RetrieveJobQueue>();
builder.Services.AddSingleton<ServiceDefault.Hosting.CancellationRegistry>();
builder.Services.AddHostedService<SpotifyRetrieveHostedService>();
builder.Services.AddHostedService<SpotifyRetrieveScheduler>();

// RabbitMQ: publishes ListeningImported after a retrieve with new plays
builder.AddCityFyMessaging();

var app = builder.Build();

// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();

app.UseAuthorization();

app.MapControllers();

app.Run();
