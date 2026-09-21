using CityFy.Retrieve.MusicBrainz.Clients;
using CityFy.Retrieve.MusicBrainz.Repositories;
using CityFy.Retrieve.MusicBrainz.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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

builder.Services.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
builder.Services.AddSingleton<IMusicBrainzRepository, MongoMusicBrainzRepository>();
builder.Services.AddSingleton<IMusicBrainzRetrieveService, MusicBrainzRetrieveService>();

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
app.UseCors("Local");
app.UseAuthorization();
app.MapControllers();
app.Run();
