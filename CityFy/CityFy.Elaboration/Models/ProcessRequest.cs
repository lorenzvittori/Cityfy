namespace CityFy.Elaboration.Models;

public class ProcessRequest
{
    /// <summary>
    /// Tag seed (es. "rock"). Se null processa tutti i grafi presenti.
    /// </summary>
    public string? SeedTag { get; set; }
}
