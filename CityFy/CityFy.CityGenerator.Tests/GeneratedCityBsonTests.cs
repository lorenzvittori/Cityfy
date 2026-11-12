using System.Text.Json;
using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.SubServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ServiceDefault.City;
using ServiceDefault.Models;

namespace CityFy.CityGenerator.Tests;

/// <summary>A generated city with its map survives the Mongo serialization (immutable records, nested lists).</summary>
public sealed class GeneratedCityBsonTests
{
    private static readonly LayoutRecipeParseSubService Recipes = new();

    [Fact]
    public void CityWithMap_RoundTripsThroughBson()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "small-organic.json"))).RootElement;
        var districts = root.GetProperty("districts").Deserialize<List<CityDistrict>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var layouts = new CityLayoutSubService();
        var map = layouts.Compute(new CityModel { Districts = districts }, Recipes.Resolve("organic", null));
        var city = new GeneratedCity
        {
            CityId = "c1", UserId = "u1", Layout = "organic", ParamsHash = "h", Status = ProcessingStatus.Done,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            City = new CityModel { UserId = "u1", Districts = districts },
            Map = map
        };

        var back = BsonSerializer.Deserialize<GeneratedCity>(city.ToBson());

        Assert.Equal(ProcessingStatus.Done, back.Status);
        Assert.Equal(JsonSerializer.Serialize(map), JsonSerializer.Serialize(back.Map));
    }
}
