using CityFy.Retrieve.LastFm.Clients;
using CityFy.Retrieve.LastFm.Consumers;
using CityFy.Retrieve.LastFm.HostedServices;
using CityFy.Retrieve.LastFm.Mappers;
using CityFy.Retrieve.LastFm.Options;
using CityFy.Retrieve.LastFm.Repositories;
using CityFy.Retrieve.LastFm.Services;
using CityFy.Retrieve.LastFm.SubServices;
using Microsoft.AspNetCore.DataProtection;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery, ProblemDetails
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Retrieve Last.fm");
builder.Services.AddCityFyCors(builder.Configuration);

// "LastFm" section validated at startup (fails fast if ApiKey or SharedSecret are missing)
builder.Services.AddOptions<LastFmOptions>()
    .BindConfiguration(LastFmOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Encrypts the stored session keys. The key ring must survive restarts, otherwise every account has
// to be linked again: in a container set DataProtection:KeysPath to a persistent volume.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("CityFy.Retrieve.LastFm");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// Shared Mongo database + writer for 'listening_events'
builder.AddCityFyMongo();
builder.Services.AddListeningEventStore();

// Last.fm API: one request gate for the whole service (limits are per IP); retries in the client
builder.Services.AddSingleton<ILastFmRequestGate, LastFmRequestGate>();
builder.Services.AddHttpClient<ILastFmClient, LastFmClient>(c => c.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddSingleton<ILastFmResponseMapper, LastFmResponseMapper>();
builder.Services.AddSingleton<ILastFmListeningMapper, LastFmListeningMapper>();
builder.Services.AddSingleton<ILastFmAccountRepository, MongoLastFmAccountRepository>();
builder.Services.AddSingleton<ILastFmImportTaskRepository, MongoLastFmImportTaskRepository>();

// Services: the flow of each use case
builder.Services.AddScoped<ILastFmAccountService, LastFmAccountService>();
builder.Services.AddScoped<ILastFmImportService, LastFmImportService>();
builder.Services.AddScoped<ILastFmPollService, LastFmPollService>();
builder.Services.AddScoped<IUserDataService, UserDataService>();

// SubServices: the logic (scoped when they use the transient typed HttpClient)
builder.Services.AddSingleton<ILastFmAccountStoreSubService, LastFmAccountStoreSubService>();
builder.Services.AddSingleton<ILastFmImportJobSubService, LastFmImportJobSubService>();
builder.Services.AddSingleton<ILastFmPollCadenceSubService, LastFmPollCadenceSubService>();
builder.Services.AddSingleton<INowPlayingStoreSubService, NowPlayingStoreSubService>();
builder.Services.AddSingleton<IUserActivitySubService, UserActivitySubService>();
builder.Services.AddSingleton<ILastFmAccountHintSubService, LastFmAccountHintSubService>();
builder.Services.AddSingleton<ILiveEventSubService, LiveEventSubService>();
builder.Services.AddSingleton<IListeningImportedEventSubService, ListeningImportedEventSubService>();
builder.Services.AddSingleton<ILastFmErrorSubService, LastFmErrorSubService>();
builder.Services.AddScoped<ILastFmAuthSubService, LastFmAuthSubService>();
builder.Services.AddScoped<ILastFmScrobbleReadSubService, LastFmScrobbleReadSubService>();

// Background work: history imports (queue + worker + recovery at startup) and the polling
builder.Services.AddSingleton<ILastFmImportQueue, LastFmImportQueue>();
builder.Services.AddSingleton<CancellationRegistry>();
builder.Services.AddHostedService<LastFmImportHostedService>();
builder.Services.AddHostedService<LastFmImportRecoveryService>();
builder.Services.AddHostedService<LastFmPollingHostedService>();

// RabbitMQ: publishes ListeningImported (source lastfm) and NowPlayingChanged; UserActive from the gateway
builder.AddCityFyMessaging();
builder.Services.AddMessageConsumer<UserActiveConsumer>();

var app = builder.Build();

// ProblemDetails errors, Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthorization();
app.MapControllers();
app.Run();
