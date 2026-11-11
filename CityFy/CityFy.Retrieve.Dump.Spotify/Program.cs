using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.HostedServices;
using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.Extensions.Options;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Retrieve Dump.Spotify");
builder.Services.AddCityFyCors(builder.Configuration);

// Bind debug upload options
builder.Services.Configure<DebugUploadOptions>(builder.Configuration.GetSection("DebugUpload"));

// Shared Mongo client/database + writer for 'listening_events'
builder.AddCityFyMongo();
builder.Services.AddListeningEventStore();

// Register file client and zip service using options
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<DebugUploadOptions>>().Value;
    // ensure root path exists
    if (string.IsNullOrWhiteSpace(opts.RootPath))
    {
        opts.RootPath = Path.Combine(Path.GetTempPath(), "CityFyDebug");
    }
    Directory.CreateDirectory(opts.RootPath);
    return new LocalFileClient(opts) as IFileClient;
});

builder.Services.AddSingleton<IStreamingRepository, MongoStreamingRepository>();

// Services
builder.Services.AddScoped<IStreamingBlPersistenceService, StreamingBlPersistenceService>();
builder.Services.AddScoped<IStreamingHistoryService, StreamingHistoryService>();
builder.Services.AddScoped<IZipUploadService, ZipUploadService>();
builder.Services.AddScoped<IUploadProcessor, UploadProcessor>();
builder.Services.AddScoped<UserDataService>();
builder.Services.AddSingleton<ServiceDefault.Hosting.CancellationRegistry>();
// Mapper
builder.Services.AddSingleton<IStreamingMapper, StreamingMapper>();

// RabbitMQ: UploadReceived -> import -> ListeningImported (to the orchestrator)
builder.AddCityFyMessaging();
builder.Services.AddMessageDeduplication();
builder.Services.AddMessageConsumer<UploadReceivedConsumer>();
builder.Services.AddHostedService<UploadRecoveryService>();

var app = builder.Build();


// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();

app.UseAuthorization();

app.MapControllers();

app.Run();
