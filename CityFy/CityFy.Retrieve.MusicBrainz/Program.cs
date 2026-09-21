using CityFy.Retrieve.MusicBrainz.Clients;
using CityFy.Retrieve.MusicBrainz.Repositories;
using CityFy.Retrieve.MusicBrainz.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

builder.Services.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
builder.Services.AddSingleton<IMusicBrainzRepository, MongoMusicBrainzRepository>();
builder.Services.AddSingleton<IMusicBrainzRetrieveService, MusicBrainzRetrieveService>();

var app = builder.Build();

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
