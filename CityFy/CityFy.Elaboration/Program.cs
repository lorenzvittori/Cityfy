using CityFy.Elaboration.Clients;
using CityFy.Elaboration.Consumers;
using CityFy.Elaboration.Options;
using CityFy.Elaboration.Repositories;
using CityFy.Elaboration.Services;
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

builder.Services.AddScoped<ICityService, CityService>();
builder.Services.AddScoped<IUserDataService, UserDataService>();

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
