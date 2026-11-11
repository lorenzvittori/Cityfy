using CityFy.Orchestrator.Consumers;
using CityFy.Orchestrator.HostedServices;
using CityFy.Orchestrator.Options;
using CityFy.Orchestrator.Repositories;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Orchestrator");
builder.Services.AddCityFyCors(builder.Configuration);

builder.Services.AddOptions<PipelineOptions>()
    .BindConfiguration(PipelineOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// 'pipeline_runs' + message deduplication
builder.AddCityFyMongo();
builder.Services.AddMessageDeduplication();
builder.Services.AddSingleton<PipelineRunRepository>();

// RabbitMQ: consumes the pipeline events, publishes BuildCityRequested
builder.AddCityFyMessaging();
builder.Services.AddMessageConsumer<ListeningImportedConsumer>();
builder.Services.AddMessageConsumer<CatalogUpdatedConsumer>();
builder.Services.AddMessageConsumer<CityBuiltConsumer>();
builder.Services.AddMessageConsumer<CityBuildFailedConsumer>();
builder.Services.AddHostedService<PipelineScheduler>();

var app = builder.Build();

// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthorization();
app.MapControllers();
app.Run();
