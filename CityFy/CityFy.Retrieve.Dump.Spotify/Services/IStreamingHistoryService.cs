using System.Threading.Tasks;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public interface IStreamingHistoryService
    {
        Task ProcessExtractedFilesAsync(string uploadId, string extractDir);
    }
}
