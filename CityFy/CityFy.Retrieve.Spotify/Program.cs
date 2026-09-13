using CityFy.Rtrieve.Spotify.Mappers;
using CityFy.RtrieveSpotify.Clients;
using CityFy.RtrieveSpotify.Mapping;
using CityFy.RtrieveSpotify.Repositories;
using CityFy.RtrieveSpotify.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Swagger/OpenAPI (Swashbuckle)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// CORS: allow local frontends (adjust origins as needed)
builder.Services.AddCors(options =>
{
    options.AddPolicy("Local", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5068", "https://localhost:7028")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// Register Spotify retrieve service (debug)
// Register client and repository which read configuration themselves
// Register AutoMapper and conversion service
builder.Services.AddAutoMapper(typeof(SpotifyMappingProfile));
builder.Services.AddSingleton<ISpotifyConverter, SpotifyConverter>();

// Bind shared Mongo options
builder.Services.Configure<ServiceDefault.Models.MongoOptions>(builder.Configuration.GetSection("Mongo"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceDefault.Models.MongoOptions>>().Value);

// Register client and repository which read configuration themselves
builder.Services.AddSingleton<ISpotifyClient, SpotifyClient>();
builder.Services.AddSingleton<ISpotifyRepository, MongoSpotifyRepository>();
builder.Services.AddSingleton<ISpotifyRetrieveService, SpotifyRetrieveService>();
// Auth client and service
builder.Services.AddSingleton<ISpotifyAuthClient, SpotifyAuthClient>();
builder.Services.AddSingleton<ISpotifyAuthService, SpotifyAuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("Local");

app.UseAuthorization();

app.MapControllers();

app.Run();
