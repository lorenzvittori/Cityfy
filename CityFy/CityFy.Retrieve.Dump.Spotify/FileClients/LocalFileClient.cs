using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.FileClients
{
    public class LocalFileClient : IFileClient
    {
        private readonly DebugUploadOptions _options;

        public LocalFileClient(DebugUploadOptions options)
        {
            _options = options;
        }

        public string GetFullPath(params string[] parts)
        {
            var all = new[] { _options.RootPath }.Concat(parts).ToArray();
            return Path.Combine(all);
        }

        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public bool Exists(string path)
        {
            return Directory.Exists(path) || System.IO.File.Exists(path);
        }

        public async Task SaveFileAsync(string path, Stream content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await using var fs = System.IO.File.Create(path);
            await content.CopyToAsync(fs);
        }

        public string[] ListFiles(string rootPath)
        {
            if (!Directory.Exists(rootPath))
                return new string[0];

            return Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(rootPath, p))
                .OrderBy(n => n)
                .ToArray();
        }
    }
}
