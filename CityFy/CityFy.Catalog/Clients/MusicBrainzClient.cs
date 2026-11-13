using System.Net.Http.Json;
using CityFy.Catalog.Models;

namespace CityFy.Catalog.Clients;

public sealed class MusicBrainzClient(HttpClient http) : IMusicBrainzClient
{
    // Same limit as ArtistResolveSubService.MaxItemsPerRequest in Retrieve.MusicBrainz
    private const int BatchSize = 1000;

    public async Task<ResolveArtistsResponse> ResolveArtistsAsync(ResolveArtistsRequest request, CancellationToken ct)
    {
        var bySpotify = new List<ResolvedArtist>();
        var byName = new List<ResolvedArtist>();
        var byMbid = new List<ResolvedArtist>();

        var batches = new[] { request.SpotifyArtistIds.Count, request.Names.Count, request.Mbids.Count }.Max(BatchesFor);

        for (var i = 0; i < batches; i++)
        {
            var batch = new ResolveArtistsRequest
            {
                SpotifyArtistIds = request.SpotifyArtistIds.Skip(i * BatchSize).Take(BatchSize).ToList(),
                Names = request.Names.Skip(i * BatchSize).Take(BatchSize).ToList(),
                Mbids = request.Mbids.Skip(i * BatchSize).Take(BatchSize).ToList()
            };

            using var resp = await http.PostAsJsonAsync("api/artists/resolve", batch, ct);
            resp.EnsureSuccessStatusCode();
            var result = await resp.Content.ReadFromJsonAsync<ResolveArtistsResponse>(ct)
                ?? throw new InvalidOperationException("Empty response from api/artists/resolve");

            bySpotify.AddRange(result.BySpotifyId);
            byName.AddRange(result.ByName);
            byMbid.AddRange(result.ByMbid ?? []);
        }

        return new ResolveArtistsResponse(bySpotify, byName, byMbid);
    }

    /// <summary>Requests needed for <paramref name="count"/> items (rounded up).</summary>
    private static int BatchesFor(int count) => (count + BatchSize - 1) / BatchSize;

    public async Task<GenreGraph> GetGenreGraphAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<GenreGraph>("api/genres/graph", ct)
        ?? throw new InvalidOperationException("Empty response from api/genres/graph");
}
