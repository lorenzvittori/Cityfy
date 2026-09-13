using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using CityFy.Retrieve.Dump.Spotify.Models;

namespace CityFy.Retrieve.Dump.Spotify.Services
{
    public interface IZipUploadService
    {
        Task<UploadResult> HandleZipUploadAsync(IFormFile file);
        UploadResult? GetUploadResult(string id);
    }
}
