using System.ComponentModel.DataAnnotations;

namespace CityFy.Retrieve.Dump.Spotify.Options;

/// <summary>"Upload" section: where uploaded exports are stored and how large they can be.</summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>Root folder of the uploaded archives and extracted files. Empty = a folder in the temp directory.</summary>
    public string RootPath { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long MaxUploadSizeBytes { get; set; } = 200_000_000;

    /// <summary>Used when <see cref="RootPath"/> is empty (local development).</summary>
    public static string DefaultRootPath => Path.Combine(Path.GetTempPath(), "CityFyUploads");
}
