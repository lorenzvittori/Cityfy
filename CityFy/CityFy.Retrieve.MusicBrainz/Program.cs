using CityFy.Retrieve.MusicBrainz.Data;
using CityFy.Retrieve.MusicBrainz.Repositories;
using CityFy.Retrieve.MusicBrainz.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
var connectionString = builder.Configuration.GetConnectionString("MusicBrainz") ?? builder.Configuration["ConnectionStrings:MusicBrainz"];
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<MusicBrainzContext>(options => options.UseSqlServer(connectionString));
}
// Http client per il download del dump
builder.Services.AddHttpClient<MbDumpClient>(c =>
{
    c.BaseAddress = new Uri("https://data.metabrainz.org/pub/musicbrainz/data/fullexport/");
    // No application-side timeout: allow long downloads without client-side cancellation
    c.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
});

// Services for processing
builder.Services.AddTransient<ArchiveExtractor>();
builder.Services.AddTransient<GenreParser>();
builder.Services.AddScoped<DataImporter>();
builder.Services.AddScoped<IGenreRepository, GenreRepository>();
builder.Services.AddScoped<RetrieveService>();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("retrieve", new Microsoft.OpenApi.OpenApiInfo { Title = "Retrieve API - MusicBrainz", Version = "v1" });
    c.SwaggerDoc("debug", new Microsoft.OpenApi.OpenApiInfo { Title = "Debug API", Version = "v1" });
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var groupName = apiDesc.GroupName ?? "";
        if (string.IsNullOrEmpty(groupName))
            return docName == "debug"; // default ungrouped endpoints go to debug
        return docName.Equals(groupName, StringComparison.OrdinalIgnoreCase) ||
               (docName == "retrieve" && groupName.StartsWith("Retrieve", StringComparison.OrdinalIgnoreCase));
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Local", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5068", "https://localhost:7028")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.Configure<ServiceDefault.Models.MongoOptions>(builder.Configuration.GetSection("Mongo"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceDefault.Models.MongoOptions>>().Value);


var app = builder.Build();

// Apply EF Core migrations at startup (Code-First)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetService<MusicBrainzContext>();
        if (db != null)
        {
            logger.LogInformation("Applying pending EF Core migrations (if any)");
            db.Database.Migrate();
            logger.LogInformation("Database migrations applied");
        }
        else
        {
            logger.LogWarning("MusicBrainzContext not registered; skipping migrations");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations");
        throw;
    }
}

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
app.UseCors("Local");
app.UseAuthorization();
app.MapControllers();
app.Run();
