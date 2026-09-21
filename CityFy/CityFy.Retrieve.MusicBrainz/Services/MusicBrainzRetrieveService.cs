using CityFy.Retrieve.MusicBrainz.Clients;
using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Repositories;

namespace CityFy.Retrieve.MusicBrainz.Services;

using Microsoft.Extensions.Logging;

public class MusicBrainzRetrieveService : IMusicBrainzRetrieveService
{
    private readonly IMusicBrainzClient _client;
    private readonly IMusicBrainzRepository _repo;
    private readonly ILogger<MusicBrainzRetrieveService> _logger;

    public MusicBrainzRetrieveService(IMusicBrainzClient client, IMusicBrainzRepository repo, ILogger<MusicBrainzRetrieveService> logger)
    {
        _client = client;
        _repo = repo;
        _logger = logger;
    }

    public async Task StartRetrieveAsync(IEnumerable<string> artistMbIds, CancellationToken cancellationToken = default)
    {
        foreach (var mbid in artistMbIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tags = (await _client.GetArtistTagsAsync(mbid, cancellationToken)).ToList();
                var model = new ArtistTags { ArtistId = mbid, Tags = tags };
                await _repo.InsertArtistTagsAsync(model);
                _logger.LogInformation("Persisted tags for artist {ArtistId} ({Count} tags)", mbid, tags?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tags for {ArtistId}", mbid);
            }
        }
    }

    public async Task<TagGraph> RetrieveRelatedTagsAndPersistAsync(string tag, int maxArtists = 50, int top = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("tag is required", nameof(tag));

        // Ottieni i tag correlati
        var related = (await _client.GetRelatedTagsAsync(tag, maxArtists, top, cancellationToken)).ToList();

        var graph = new TagGraph
        {
            SeedTag = tag,
            RetrievedAtUtc = DateTime.UtcNow,
            RelatedTags = related.Select(t => new RelatedTag { Tag = t.Tag, Count = t.Count }).ToList()
        };

        // Persisti
        await _repo.InsertRelatedTagsAsync(graph);

        return graph;
    }
}
