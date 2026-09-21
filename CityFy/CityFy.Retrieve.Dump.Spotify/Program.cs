using CityFy.Retrieve.Dump.Spotify.FileClients;
using ServiceDefault.Logging;
using CityFy.Retrieve.Dump.Spotify.HostedServices;
using CityFy.Retrieve.Dump.Spotify.Mappers;
using CityFy.Retrieve.Dump.Spotify.Models;
using CityFy.Retrieve.Dump.Spotify.Repositories;
using CityFy.Retrieve.Dump.Spotify.Services;
using Swashbuckle.AspNetCore.Swagger;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ServiceDefault.Models;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure logging using shared extension
builder.Services.AddAppLogging(builder.Configuration);

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Swagger/OpenAPI (Swashbuckle)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("retrieve", new OpenApiInfo { Title = "Retrieve API - Dump.Spotify", Version = "v1" });
    c.SwaggerDoc("debug", new OpenApiInfo { Title = "Debug API", Version = "v1" });
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var groupName = apiDesc.GroupName ?? "";
        if (string.IsNullOrEmpty(groupName))
            return docName == "debug";
        return docName.Equals(groupName, StringComparison.OrdinalIgnoreCase) ||
               (docName == "retrieve" && groupName.StartsWith("Retrieve", StringComparison.OrdinalIgnoreCase));
    });
});

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
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/retrieve/swagger.json", "Retrieve API");
        c.SwaggerEndpoint("/swagger/debug/swagger.json", "Debug API");
    });
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
