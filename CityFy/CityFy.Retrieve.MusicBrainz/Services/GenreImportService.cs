using CityFy.Retrieve.MusicBrainz.Models;
using CityFy.Retrieve.MusicBrainz.Repositories;
using Microsoft.Extensions.Logging;

namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IGenreImportService
{
    Task<Dictionary<Genre, Genre>> ImportGenresAsync(GenreParser.ParsedResult parsed, CancellationToken cancellationToken = default);
    Task<int> ImportRelationsAsync(GenreParser.ParsedResult parsed, Dictionary<Genre, Genre> mapping, CancellationToken cancellationToken = default);
}

public class GenreImportService : IGenreImportService
{
    private readonly IGenreRepository _repo;
    private readonly ILogger<GenreImportService> _logger;

    public GenreImportService(IGenreRepository repo, ILogger<GenreImportService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<Dictionary<Genre, Genre>> ImportGenresAsync(GenreParser.ParsedResult parsed, CancellationToken cancellationToken = default)
    {
        var mapping = new Dictionary<Genre, Genre>();
        int addedGenres = 0;

        foreach (var src in parsed.Genres)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Genre? found = null;
            try
            {
                if (!string.IsNullOrEmpty(src.MusicBrainzId))
                {
                    found = await _repo.FindByMusicBrainzIdAsync(src.MusicBrainzId, cancellationToken);
                }
                if (found == null)
                {
                    found = await _repo.FindByNameAsync(src.Name, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while searching for genre '{Name}'", src.Name);
            }

            if (found == null)
            {
                var toAdd = new Genre { Name = src.Name, Description = src.Description, MusicBrainzId = src.MusicBrainzId };
                try
                {
                    found = await _repo.AddAsync(toAdd, cancellationToken);
                    addedGenres++;
                    _logger.LogDebug("Added genre {Name} with Id {Id}", found.Name, found.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add genre {Name}", src.Name);
                    continue;
                }
            }

            mapping[src] = found;
        }

        _logger.LogInformation("Genres processed: {Processed}, added: {Added}", parsed.Genres.Count, addedGenres);
        return mapping;
    }

    public async Task<int> ImportRelationsAsync(GenreParser.ParsedResult parsed, Dictionary<Genre, Genre> mapping, CancellationToken cancellationToken = default)
    {
        int addedRelations = 0;
        foreach (var rel in parsed.Relations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!mapping.TryGetValue(rel.Parent, out var dbParent) || !mapping.TryGetValue(rel.Child, out var dbChild))
            {
                _logger.LogDebug("Skipping relation because parent/child mapping missing: parent={Parent}, child={Child}", rel.Parent?.Name, rel.Child?.Name);
                continue;
            }

            try
            {
                var exists = await _repo.RelationExistsAsync(dbParent.Id, dbChild.Id, rel.RelationType, cancellationToken);
                if (!exists)
                {
                    var newRel = new GenreRelation { ParentId = dbParent.Id, ChildId = dbChild.Id, RelationType = rel.RelationType };
                    await _repo.AddRelationAsync(newRel, cancellationToken);
                    addedRelations++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add relation parent={ParentId} child={ChildId}", dbParent.Id, dbChild.Id);
            }
        }

        _logger.LogInformation("Relations processed: {Processed}, added: {Added}", parsed.Relations.Count, addedRelations);
        return addedRelations;
    }
}
