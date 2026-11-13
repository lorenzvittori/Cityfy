using CityFy.Retrieve.Dump.Spotify.Consumers;
using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.HostedServices;
using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Options;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using CityFy.Retrieve.Dump.Spotify.Services;
using CityFy.Retrieve.Dump.Spotify.SubServices;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Retrieve Dump.Spotify");
builder.Services.AddCityFyCors(builder.Configuration);

// "Upload" section validated at startup (storage folder, size limit)
builder.Services.AddOptions<UploadOptions>()
    .BindConfiguration(UploadOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Shared Mongo client/database + writer for 'listening_events'
builder.AddCityFyMongo();
builder.Services.AddListeningEventStore();

// Uploaded archives and extracted files on the local disk (a persistent volume in a container)
builder.Services.AddSingleton<IFileClient, LocalFileClient>();
builder.Services.AddSingleton<IStreamingRepository, MongoStreamingRepository>();
builder.Services.AddSingleton<IStreamingMapper, StreamingMapper>();

// Services: the flow of each use case
builder.Services.AddScoped<IZipUploadService, ZipUploadService>();
builder.Services.AddScoped<IUploadTaskService, UploadTaskService>();
builder.Services.AddScoped<IUploadProcessor, UploadProcessor>();
builder.Services.AddScoped<IUserDataService, UserDataService>();

// SubServices: the logic
builder.Services.AddSingleton<IUploadValidationSubService, UploadValidationSubService>();
builder.Services.AddSingleton<IUploadStorageSubService, UploadStorageSubService>();
builder.Services.AddSingleton<IUploadTaskSubService, UploadTaskSubService>();
builder.Services.AddSingleton<IStreamingHistoryImportSubService, StreamingHistoryImportSubService>();
builder.Services.AddSingleton<ILegacyUploadMigrationSubService, LegacyUploadMigrationSubService>();
builder.Services.AddSingleton<IUploadDocumentsSubService, UploadDocumentsSubService>();
builder.Services.AddSingleton<IUploadEventsSubService, UploadEventsSubService>();
builder.Services.AddSingleton<IUploadErrorSubService, UploadErrorSubService>();

// Running imports, so the admin can stop them
builder.Services.AddSingleton<CancellationRegistry>();

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
