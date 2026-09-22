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
    private readonly IConfiguration _configuration;

    public MusicBrainzRetrieveService(IMusicBrainzClient client, IMusicBrainzRepository repo, ILogger<MusicBrainzRetrieveService> logger, IConfiguration configuration)
    {
        _client = client;
        _repo = repo;
        _logger = logger;
        _configuration = configuration;
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

    public async Task<TagGraph> RetrieveRelatedTagsAndPersistAsync(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("tag is required", nameof(tag));
        // read page delay from configuration (seconds)
        var delaySec = 1;
        var cfg = _configuration["MusicBrainz:PageDelaySeconds"];
        if (!string.IsNullOrWhiteSpace(cfg) && int.TryParse(cfg, out var parsed)) delaySec = Math.Max(0, parsed);

        // Callback to persist intermediate aggregated results after each page
        Func<int, IDictionary<string, int>, CancellationToken, Task> onPage = async (pageNum, counts, ct) =>
        {
            var graph = new TagGraph
            {
                SeedTag = tag,
                RetrievedAtUtc = DateTime.UtcNow,
                RelatedTags = counts.Select(kv => new RelatedTag { Tag = kv.Key, Count = kv.Value }).ToList()
            };
            try
            {
                await _repo.InsertRelatedTagsAsync(graph);
                _logger.LogInformation("Persisted intermediate TagGraph for {Tag} page={Page} (entries={Count})", tag, pageNum, counts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist intermediate TagGraph for {Tag} page={Page}", tag, pageNum);
            }
        };

        // Ottieni i tag correlati con callback per pagina
        var related = (await _client.GetRelatedTagsAsync(tag, delaySec, onPage)).ToList();

        // Persist final graph
        var finalGraph = new TagGraph
        {
            SeedTag = tag,
            RetrievedAtUtc = DateTime.UtcNow,
            RelatedTags = related.Select(t => new RelatedTag { Tag = t.Tag, Count = t.Count }).ToList()
        };
        await _repo.InsertRelatedTagsAsync(finalGraph);
        _logger.LogInformation("Persisted final TagGraph for {Tag} (entries={Count})", tag, finalGraph.RelatedTags?.Count ?? 0);

        return finalGraph;
    }
}
