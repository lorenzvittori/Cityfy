namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public interface IUploadValidationSubService
{
    /// <summary>
    /// Checks the uploaded export (present, .zip, within Upload:MaxUploadSizeBytes) and its owner.
    /// Throws UPLOAD_* CityFyExceptions. Returns the trimmed user id.
    /// </summary>
    string Validate(IFormFile? file, string? userId);

    /// <summary>Owner of an upload: throws UPLOAD_USER_MISSING when empty. Returns the trimmed user id.</summary>
    string ValidateOwner(string? userId);
}