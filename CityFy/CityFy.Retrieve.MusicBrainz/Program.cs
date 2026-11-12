using CityFy.Retrieve.MusicBrainz.Clients;
using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.EntityFrameworkCore;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("Retrieve MusicBrainz");
builder.Services.AddCityFyCors(builder.Configuration);

// Aspire client integration: connection string "ConnectionStrings:musicbrainz" (injected by the
// AppHost), retries, health check and tracing included
builder.AddSqlServerDbContext<MusicBrainzContext>("musicbrainz");

// Dump download: no client timeout, the archives are several GB
builder.Services.AddHttpClient<IMbDumpClient, MbDumpClient>(c =>
{
    c.BaseAddress = new Uri("https://data.metabrainz.org/pub/musicbrainz/data/fullexport/");
    c.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
});

// Retrieve pipeline: download -> extract -> import (background, one at a time)
builder.Services.AddScoped<IDumpDownloadService, DumpDownloadService>();
builder.Services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
builder.Services.AddScoped<IMusicBrainzImporter, MusicBrainzImporter>();
builder.Services.AddScoped<IRetrieveService, RetrieveService>();
builder.Services.AddSingleton<ImportProgress>();
// RabbitMQ: CatalogUpdated after an import, so the orchestrator rebuilds every city
builder.AddCityFyMessaging();
builder.Services.AddSingleton<RetrieveRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RetrieveRunner>());

// Read API
builder.Services.AddScoped<IMusicBrainzQueryService, MusicBrainzQueryService>();

var app = builder.Build();

// Apply EF Core migrations at startup (Code-First)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Applying pending EF Core migrations (if any)");
    scope.ServiceProvider.GetRequiredService<MusicBrainzContext>().Database.Migrate();
}

// Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthorization();
app.MapControllers();
app.Run();
