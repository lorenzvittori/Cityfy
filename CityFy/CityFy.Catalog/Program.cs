using CityFy.Catalog.Clients;
using CityFy.Catalog.Consumers;
using CityFy.Catalog.Options;
using CityFy.Catalog.Repositories;
using CityFy.Catalog.Services;
using CityFy.Catalog.SubServices;
using Microsoft.Extensions.Options;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Catalog");
builder.Services.AddCityFyCors(builder.Configuration);
builder.Services.AddMemoryCache();

builder.Services.AddOptions<CityOptions>()
    .BindConfiguration(CityOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<MusicBrainzServiceOptions>()
    .BindConfiguration(MusicBrainzServiceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Reads 'listening_events', writes 'user_catalogs', 'user_artist_placements'
builder.AddCityFyMongo();
builder.Services.AddSingleton<ICatalogRepository, MongoCatalogRepository>();
// Change of owner of the plays (anonymous session -> account): Catalog owns their deletion, and their moves
builder.Services.AddListeningEventTransfer();

// Service-to-service call: standard resilience (retry, circuit breaker, timeouts) is appropriate here
builder.Services.AddHttpClient<IMusicBrainzClient, MusicBrainzClient>((sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MusicBrainzServiceOptions>>().Value.BaseUrl))
    .AddStandardResilienceHandler();

// Services: the flow of each use case
builder.Services.AddScoped<ICatalogRefreshService, CatalogRefreshService>();
builder.Services.AddScoped<IUserDataService, UserDataService>();
builder.Services.AddScoped<ICityModelService, CityModelService>();

// SubServices: the logic (stateless ones are singletons; those on typed HttpClients are scoped)
builder.Services.AddSingleton<ICityBuildSubService, CityBuildSubService>();
builder.Services.AddSingleton<ICatalogRefreshErrorSubService, CatalogRefreshErrorSubService>();
builder.Services.AddSingleton<IListeningAggregationSubService, ListeningAggregationSubService>();
builder.Services.AddSingleton<IUserDataDeletionSubService, UserDataDeletionSubService>();
builder.Services.AddSingleton<IUserDataTransferSubService, UserDataTransferSubService>();
builder.Services.AddSingleton<IArtistPlacementSubService, ArtistPlacementSubService>();
builder.Services.AddSingleton<IUserCatalogStoreSubService, UserCatalogStoreSubService>();
builder.Services.AddSingleton<IDateRangeSubService, DateRangeSubService>();
builder.Services.AddSingleton<IGenreOptionsSubService, GenreOptionsSubService>();
builder.Services.AddScoped<IArtistResolveSubService, ArtistResolveSubService>();
builder.Services.AddScoped<IGenreHierarchySubService, GenreHierarchySubService>();
builder.Services.AddScoped<ICatalogEventsSubService, CatalogEventsSubService>();

// RabbitMQ: RefreshCatalogRequested (from the orchestrator) -> UserCatalogUpdated | CatalogRefreshFailed
builder.AddCityFyMessaging();
builder.Services.AddMessageDeduplication();
builder.Services.AddMessageConsumer<RefreshCatalogRequestedConsumer>();

var app = builder.Build();

// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthorization();
app.MapControllers();
app.Run();
