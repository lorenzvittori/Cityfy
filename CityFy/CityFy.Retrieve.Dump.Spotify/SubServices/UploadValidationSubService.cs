using CityFy.Retrieve.Dump.Spotify.Options;
using Microsoft.Extensions.Options;
using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class UploadValidationSubService(IOptions<UploadOptions> options) : IUploadValidationSubService
{
    public string Validate(IFormFile? file, string? userId)
    {
        if (file == null)
            throw new CityFyException(ErrorCodes.UploadFileMissing, "Missing file form field (use 'file')");
        var owner = ValidateOwner(userId);

        var limit = options.Value.MaxUploadSizeBytes;
        if (file.Length > limit)
            throw new CityFyException(ErrorCodes.UploadTooLarge, $"File too large: {file.Length} bytes, limit {limit}", StatusCodes.Status413PayloadTooLarge);

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new CityFyException(ErrorCodes.UploadNotZip, "Only .zip files are accepted");

        return owner;
    }

    public string ValidateOwner(string? userId) =>
        string.IsNullOrWhiteSpace(userId)
            ? throw new CityFyException(ErrorCodes.UploadUserMissing, "Missing userId (owner of the export)")
            : userId.Trim();
}