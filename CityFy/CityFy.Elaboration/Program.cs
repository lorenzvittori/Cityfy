using CityFy.Elaboration.Clients;
using CityFy.Elaboration.Consumers;
using CityFy.Elaboration.Options;
using CityFy.Elaboration.Repositories;
using CityFy.Elaboration.Services;
using CityFy.Elaboration.SubServices;
using Microsoft.Extensions.Options;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Elaboration");
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

// Reads 'listening_events', writes 'city_models'
builder.AddCityFyMongo();
builder.Services.AddSingleton<IElaborationRepository, MongoElaborationRepository>();

// Service-to-service call: standard resilience (retry, circuit breaker, timeouts) is appropriate here
builder.Services.AddHttpClient<IMusicBrainzClient, MusicBrainzClient>((sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MusicBrainzServiceOptions>>().Value.BaseUrl))
    .AddStandardResilienceHandler();

// Services: the flow of each use case
builder.Services.AddScoped<ICityService, CityService>();
builder.Services.AddScoped<IUserDataService, UserDataService>();

// SubServices: the logic (stateless ones are singletons; those on typed HttpClients are scoped)
builder.Services.AddSingleton<IPeriodSubService, PeriodSubService>();
builder.Services.AddSingleton<ICityBuildSubService, CityBuildSubService>();
builder.Services.AddSingleton<ICityBuildErrorSubService, CityBuildErrorSubService>();
builder.Services.AddSingleton<IListeningAggregationSubService, ListeningAggregationSubService>();
builder.Services.AddSingleton<ICityStoreSubService, CityStoreSubService>();
builder.Services.AddSingleton<IUserDataDeletionSubService, UserDataDeletionSubService>();
builder.Services.AddScoped<IArtistResolveSubService, ArtistResolveSubService>();
builder.Services.AddScoped<IGenreHierarchySubService, GenreHierarchySubService>();
builder.Services.AddScoped<ICityBuildEventsSubService, CityBuildEventsSubService>();

// RabbitMQ: BuildCityRequested (from the orchestrator) -> CityBuilt | CityBuildFailed
builder.AddCityFyMessaging();
builder.Services.AddMessageDeduplication();
builder.Services.AddMessageConsumer<BuildCityRequestedConsumer>();

var app = builder.Build();

// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthorization();
app.MapControllers();
app.Run();
