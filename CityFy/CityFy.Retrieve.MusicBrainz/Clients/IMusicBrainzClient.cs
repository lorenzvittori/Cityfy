namespace CityFy.Retrieve.MusicBrainz.Clients;

public interface IMusicBrainzClient
{
    /// <summary>
    /// Recupera i tag (nomi) associati a un artista dato il suo MBID.
    /// </summary>
    Task<IEnumerable<string>> GetArtistTagsAsync(string artistMbId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dato un tag (es. "rock"), recupera tag correlati aggregando i tag
    /// presenti negli artisti associati a quel tag. Restituisce i top N tag con conteggio.
    /// </summary>
    /// <summary>
    /// Recupera tag correlati per il seed 'tag'. Esegue paginazione interna e chiama opzionalmente un callback asincrono dopo ogni pagina elaborata.
    /// pageDelaySeconds: pausa tra le pagine per non sovraccaricare MusicBrainz.
    /// onPageAggregatedAsync: callback invocato con il numero di pagina e il dizionario conteggi aggregati finora.
    /// I parametri operativi come maxArtists e top sono configurati nel client tramite configurazione.
    /// </summary>
    Task<IEnumerable<(string Tag, int Count)>> GetRelatedTagsAsync(
        string tag,
        int pageDelaySeconds = 1,
        Func<int, IDictionary<string, int>, CancellationToken, Task>? onPageAggregatedAsync = null,
        CancellationToken cancellationToken = default);
}
