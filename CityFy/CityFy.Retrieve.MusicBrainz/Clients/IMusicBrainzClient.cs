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
    Task<IEnumerable<(string Tag, int Count)>> GetRelatedTagsAsync(string tag, int maxArtists = 50, int top = 20, CancellationToken cancellationToken = default);
}
