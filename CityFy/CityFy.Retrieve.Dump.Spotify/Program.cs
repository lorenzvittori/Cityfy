using CityFy.Retrieve.Dump.Spotify.FileClients;
using CityFy.Retrieve.Dump.Spotify.HostedServices;
using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using CityFy.Retrieve.Dump.Spotify.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ServiceDefault.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Swagger/OpenAPI (Swashbuckle)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Bind debug upload options
builder.Services.Configure<DebugUploadOptions>(builder.Configuration.GetSection("DebugUpload"));
// Bind Mongo options (shared)
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection("Mongo"));

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

// Register Mongo options instance and repository
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MongoOptions>>().Value);
builder.Services.AddSingleton<IStreamingRepository, MongoStreamingRepository>();

// Register services
// Persistence subservice for BL models
builder.Services.TryAddScoped<IStreamingBlPersistenceService, StreamingBlPersistenceService>();
builder.Services.AddScoped<IStreamingHistoryService, StreamingHistoryService>();
builder.Services.AddScoped<IZipUploadService, ZipUploadService>();
// Mapper
builder.Services.AddSingleton<IStreamingMapper, StreamingMapper>();
// Background hosted service to process extracted uploads periodically
builder.Services.AddHostedService<UploadProcessingHostedService>();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
