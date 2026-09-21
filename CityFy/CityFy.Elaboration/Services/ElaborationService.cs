using CityFy.Elaboration.Models;
using CityFy.Elaboration.Repositories;
using Microsoft.Extensions.Logging;

namespace CityFy.Elaboration.Services;

public class ElaborationService : IElaborationService
{
    private readonly IElaborationRepository _repo;
    private readonly ILogger<ElaborationService> _logger;

    public ElaborationService(IElaborationRepository repo, ILogger<ElaborationService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<IEnumerable<Graph>> ProcessAsync(string? seedTag = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting elaboration process (seed={Seed})", seedTag);

        var tagGraphs = await _repo.GetTagGraphsAsync(seedTag);
        var results = new List<Graph>();

        foreach (var tg in tagGraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var related = tg.RelatedTags ?? new List<RelatedTag>();

                // Create nodes: seed + related, weights normalized
                var max = related.Any() ? related.Max(r => r.Count) : 1;

                var nodes = new List<Node>();
                nodes.Add(new Node { Tag = tg.SeedTag, Weight = 1.0 });
                foreach (var r in related)
                {
                    var w = max > 0 ? (double)r.Count / max : 0.0;
                    nodes.Add(new Node { Tag = r.Tag, Weight = w });
                }

                // Edges: from seed to each related tag
                var edges = related.Select(r => new Edge { From = tg.SeedTag, To = r.Tag, Weight = (double)r.Count }).ToList();

                var graph = new Graph
                {
                    SeedTag = tg.SeedTag,
                    GeneratedAtUtc = DateTime.UtcNow,
                    Nodes = nodes,
                    Edges = edges
                };

                await _repo.InsertGraphAsync(graph);
                results.Add(graph);

                _logger.LogInformation("Processed TagGraph seed={Seed} -> produced {Nodes} nodes", tg.SeedTag, nodes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process TagGraph seed={Seed}", tg.SeedTag);
            }
        }

        return results;
    }
}
