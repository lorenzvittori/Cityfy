using CityFy.Api.Auth;
using CityFy.Api.Clients;
using CityFy.Api.Options;
using ServiceDefault.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Logging, OpenTelemetry, health checks, service discovery, ProblemDetails
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddCityFySwagger("CityFy");
builder.Services.AddCityFyCors(builder.Configuration);
builder.Services.AddCityFyAuth(builder.Environment);

builder.Services.AddOptions<FrontendOptions>()
    .BindConfiguration(FrontendOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Internal services, resolved by Aspire service discovery (resource names of the AppHost)
static void AddService<TClient>(IServiceCollection services, string resource, TimeSpan? uploadTimeout = null) where TClient : class
{
    var http = services.AddHttpClient<TClient>(c =>
    {
        c.BaseAddress = new Uri($"https+http://{resource}");
        if (uploadTimeout.HasValue) c.Timeout = uploadTimeout.Value;
    });

    // Standard resilience (retries, 30s total timeout) only for ordinary calls
    if (!uploadTimeout.HasValue)
        http.AddStandardResilienceHandler();
}

AddService<ElaborationClient>(builder.Services, "Elaboration");
AddService<OrchestratorClient>(builder.Services, "Orchestrator");
AddService<SpotifyServiceClient>(builder.Services, "RetrieveSpotify");
// Uploads: large bodies, no automatic retry, long timeout
AddService<DumpClient>(builder.Services, "RetrieveDumpSpotify", uploadTimeout: TimeSpan.FromMinutes(10));

var app = builder.Build();

// ProblemDetails errors, Swagger (dev), HTTPS, CORS, /health and /alive
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
