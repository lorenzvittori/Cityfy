using System.Net.Http.Json;
using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Clients;

public interface IMusicBrainzClient
{
    /// <summary>Resolves any number of artists (split in batches of the API limit).</summary>
    Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct);

    Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct);
}

public class MusicBrainzClient : IMusicBrainzClient
{
    // Same limit as MusicBrainzQueryService.MaxItemsPerRequest
    private const int BatchSize = 1000;

    private readonly HttpClient _http;

    public MusicBrainzClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct)
    {
        var bySpotify = new List<ResolvedArtist>();
        var byName = new List<ResolvedArtist>();

        var batches = Math.Max(
            (request.SpotifyArtistIds.Count + BatchSize - 1) / BatchSize,
            (request.Names.Count + BatchSize - 1) / BatchSize);

        for (var i = 0; i < batches; i++)
        {
            var batch = new ResolveArtistsRequest
            {
                SpotifyArtistIds = request.SpotifyArtistIds.Skip(i * BatchSize).Take(BatchSize).ToList(),
                Names = request.Names.Skip(i * BatchSize).Take(BatchSize).ToList()
            };

            using var resp = await _http.PostAsJsonAsync("api/artists/resolve", batch, ct);
            resp.EnsureSuccessStatusCode();
            var result = await resp.Content.ReadFromJsonAsync<ResolveArtistsResponse>(ct)
                ?? throw new InvalidOperationException("Empty response from api/artists/resolve");

            bySpotify.AddRange(result.BySpotifyId);
            byName.AddRange(result.ByName);
        }

        return new ResolveArtistsResponse(bySpotify, byName);
    }

    public async Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct) =>
        await _http.GetFromJsonAsync<GenreGraph>("api/genres/graph", ct)
        ?? throw new InvalidOperationException("Empty response from api/genres/graph");
}
