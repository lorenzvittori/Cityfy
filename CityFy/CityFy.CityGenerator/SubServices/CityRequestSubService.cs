using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CityFy.CityGenerator.Models;
using CityFy.CityGenerator.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.City;
using ServiceDefault.Errors;

namespace CityFy.CityGenerator.SubServices;

public sealed class CityRequestSubService(IOptions<CityGeneratorOptions> options) : ICityRequestSubService
{
    private static readonly HashSet<string> Layouts = [CityRequest.Organic, CityRequest.Grid, CityRequest.Custom];

    private readonly CityGeneratorOptions _options = options.Value;

    public CityRequest Validate(GenerateCityRequest request, DateTime now)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        if (name?.Length > _options.MaxNameLength)
            throw Invalid(ErrorCodes.CityInvalidName, $"name longer than {_options.MaxNameLength} characters");

        var from = ToUtc(request.From);
        var to = ToUtc(request.To);
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
            throw Invalid(ErrorCodes.CityInvalidRange, $"from ({from:O}) must be before to ({to:O})");
        if (from.HasValue && from.Value > now)
            throw Invalid(ErrorCodes.CityInvalidRange, $"from ({from:O}) is in the future");

        var layout = string.IsNullOrWhiteSpace(request.Layout) ? CityRequest.Organic : request.Layout.Trim().ToLowerInvariant();
        if (!Layouts.Contains(layout))
            throw Invalid(ErrorCodes.CityInvalidLayout, $"layout '{request.Layout}' is not available (use {string.Join(", ", Layouts)})");

        var genres = (request.Genres ?? []).Distinct().Order().ToList();
        var subgenres = (request.Subgenres ?? []).Distinct().Order().ToList();
        if (genres.Count + subgenres.Count > _options.MaxGenres)
            throw Invalid(ErrorCodes.CityInvalidGenres, $"more than {_options.MaxGenres} genres and subgenres");

        // The parameter string only matters for a custom map (validated by the recipe parser)
        var recipe = layout == CityRequest.Custom ? request.Recipe : null;
        return new CityRequest(name, from, to, layout, recipe, genres, subgenres);
    }

    public string Hash(CityRequest request)
    {
        var canonical = string.Join('|',
            Date(request.From), Date(request.To), request.Layout, request.Recipe ?? "",
            string.Join(',', request.Genres), string.Join(',', request.Subgenres));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Date(DateTime? d) => d?.ToString("O", CultureInfo.InvariantCulture) ?? "";

    private static DateTime? ToUtc(DateTime? d) => d?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d.Value, DateTimeKind.Utc)
    };

    private static CityFyException Invalid(string code, string message) => new(code, message, StatusCodes.Status400BadRequest);
}
