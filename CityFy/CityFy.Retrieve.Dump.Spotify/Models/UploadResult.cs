namespace CityFy.Retrieve.Dump.Spotify.Models;

public sealed class UploadResult
{
    public string Id { get; set; } = null!;
    public string UploadPath { get; set; } = null!;
    public string ExtractedPath { get; set; } = null!;
    public string[] Files { get; set; } = null!;
}
