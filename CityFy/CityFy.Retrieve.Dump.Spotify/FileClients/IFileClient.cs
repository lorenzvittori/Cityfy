using System.IO;
using System.Threading.Tasks;

namespace CityFy.Retrieve.Dump.Spotify.FileClients
{
    public interface IFileClient
    {
        Task SaveFileAsync(string path, Stream content);
        void CreateDirectory(string path);
        bool Exists(string path);
        string[] ListFiles(string rootPath);
        string GetFullPath(params string[] parts);
    }
}
