using CityFy.Retrieve.Dump.Spotify.Options;
using Microsoft.Extensions.Options;

namespace CityFy.Retrieve.Dump.Spotify.FileClients;

/// <summary>Uploads on the local file system, under Upload:RootPath (created at startup).</summary>
public sealed class LocalFileClient : IFileClient
{
    private readonly string _rootPath;

    public LocalFileClient(IOptions<UploadOptions> options)
    {
        _rootPath = string.IsNullOrWhiteSpace(options.Value.RootPath) ? UploadOptions.DefaultRootPath : options.Value.RootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public string GetFullPath(params string[] parts) => Path.Combine([_rootPath, .. parts]);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    public async Task SaveFileAsync(string path, Stream content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(path);
        await content.CopyToAsync(fs);
    }

    public string[] ListFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            return [];

        return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(rootPath, p))
            .OrderBy(n => n)
            .ToArray();
    }
}
