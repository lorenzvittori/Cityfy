namespace CityFy.Elaboration.Models;

public class ProcessRequest
{
    /// <summary>
    /// Tag seed (es. "rock"). Se null processa tutti i grafi presenti.
    /// </summary>
    public string? SeedTag { get; set; }

    /// <summary>
    /// Lista di TagGraph forniti come input all'elaborazione. Se specificata verrà usata direttamente.
    /// </summary>
    public IEnumerable<TagGraph>? TagGraphs { get; set; }
}
