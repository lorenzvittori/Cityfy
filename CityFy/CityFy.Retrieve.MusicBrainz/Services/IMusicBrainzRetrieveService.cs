namespace CityFy.Retrieve.MusicBrainz.Services;

public interface IMusicBrainzRetrieveService
{
    /// <summary>
    /// Avvia il retrieve dei tag per la lista di artist MBID fornita.
    /// </summary>
    Task StartRetrieveAsync(IEnumerable<string> artistMbIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera tag correlati per un tag seed, persiste il grafo in Mongo e ritorna il TagGraph.
    /// </summary>
    Task<CityFy.Retrieve.MusicBrainz.Models.TagGraph> RetrieveRelatedTagsAndPersistAsync(string tag, int maxArtists = 50, int top = 20, CancellationToken cancellationToken = default);
}
