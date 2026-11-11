namespace CityFy.Retrieve.Dump.Spotify.Models
{
    // DTO used for Swagger-friendly file upload binding
    public class UploadZipApi
    {
        public IFormFile? File { get; set; }

        // Owner of the export (Spotify user id until a CityFy identity exists).
        // The extended streaming history files do not contain it.
        public string? UserId { get; set; }
    }
}
