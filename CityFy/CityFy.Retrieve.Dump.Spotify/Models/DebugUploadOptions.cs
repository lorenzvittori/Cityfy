namespace CityFy.Retrieve.Dump.Spotify.Models
{
    public class DebugUploadOptions
    {
        // Root folder where uploads and extracted files are stored
        public string RootPath { get; set; } = null!;

        // Maximum allowed upload size in bytes
        public long MaxUploadSizeBytes { get; set; } = 200_000_000; // 200 MB default
    }
}
