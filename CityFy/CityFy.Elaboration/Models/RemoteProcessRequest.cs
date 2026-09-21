namespace CityFy.Elaboration.Models;

public class RemoteProcessRequest
{
    /// <summary>
    /// Base URL del servizio retrieve (es. https://localhost:5001)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Dimensione pagina da richiedere (default 50)
    /// </summary>
    public int PageSize { get; set; } = 50;
}
