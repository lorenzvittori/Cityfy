using CityFy.CityGenerator.Clients;
using CityFy.CityGenerator.Consumers;
using CityFy.CityGenerator.HostedServices;
using CityFy.CityGenerator.Mappers;
using CityFy.CityGenerator.Options;
using CityFy.CityGenerator.Repositories;
using CityFy.CityGenerator.Services;
using CityFy.CityGenerator.SubServices;
using Microsoft.Extensions.Options;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("CityGenerator");
builder.Services.AddCityFyCors(builder.Configuration);

// Limits of the generated cities, validated at startup
builder.Services.AddOptions<CityGeneratorOptions>()
    .BindConfiguration(CityGeneratorOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<CatalogServiceOptions>()
    .BindConfiguration(CatalogServiceOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Owns 'generated_cities'
builder.AddCityFyMongo();
builder.Services.AddSingleton<IGeneratedCityRepository, MongoGeneratedCityRepository>();

// The city data comes from Catalog: service-to-service call with standard resilience
builder.Services.AddHttpClient<ICatalogClient, CatalogClient>((sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<CatalogServiceOptions>>().Value.BaseUrl))
    .AddStandardResilienceHandler();

// Services: the flow of each use case
builder.Services.AddScoped<ICityGenerationService, CityGenerationService>();
builder.Services.AddScoped<IUserDataService, UserDataService>();

// SubServices: the logic (those on typed HttpClients are scoped)
builder.Services.AddSingleton<ICityRequestSubService, CityRequestSubService>();
builder.Services.AddSingleton<IGeneratedCityStoreSubService, GeneratedCityStoreSubService>();
builder.Services.AddSingleton<IGenerationEventsSubService, GenerationEventsSubService>();
builder.Services.AddSingleton<IGenerationErrorSubService, GenerationErrorSubService>();
builder.Services.AddSingleton<IGeneratedCityMapper, GeneratedCityMapper>();
builder.Services.AddSingleton<ICityLayoutSubService, CityLayoutSubService>();
builder.Services.AddSingleton<ILayoutRecipeParseSubService, LayoutRecipeParseSubService>();
builder.Services.AddScoped<ICityModelFetchSubService, CityModelFetchSubService>();

// Running generations, so the user can stop them
builder.Services.AddSingleton<CancellationRegistry>();

// RabbitMQ: GenerateCityRequested (itself) -> generated city; UserCatalogUpdated (Catalog) -> first city
builder.AddCityFyMessaging();
builder.Services.AddMessageDeduplication();
builder.Services.AddMessageConsumer<GenerateCityRequestedConsumer>();
builder.Services.AddMessageConsumer<UserCatalogUpdatedConsumer>();
builder.Services.AddHostedService<GenerationRecoveryService>();

var app = builder.Build();

// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
