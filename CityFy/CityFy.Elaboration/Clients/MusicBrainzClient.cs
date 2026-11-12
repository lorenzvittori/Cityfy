using System.Net.Http.Json;
using CityFy.Elaboration.Models;

namespace CityFy.Elaboration.Clients;

public sealed class MusicBrainzClient(HttpClient http) : IMusicBrainzClient
{
    // Same limit as ArtistResolveSubService.MaxItemsPerRequest in Retrieve.MusicBrainz
    private const int BatchSize = 1000;

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

            using var resp = await http.PostAsJsonAsync("api/artists/resolve", batch, ct);
            resp.EnsureSuccessStatusCode();
            var result = await resp.Content.ReadFromJsonAsync<ResolveArtistsResponse>(ct)
                ?? throw new InvalidOperationException("Empty response from api/artists/resolve");

            bySpotify.AddRange(result.BySpotifyId);
            byName.AddRange(result.ByName);
        }

        return new ResolveArtistsResponse(bySpotify, byName);
    }

    public async Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<GenreGraph>("api/genres/graph", ct)
        ?? throw new InvalidOperationException("Empty response from api/genres/graph");
}
