namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public interface IUploadErrorSubService
{
    /// <summary>ErrorCodes value of a failed import (UPLOAD_PROCESSING_FAILED when not a CityFyException).</summary>
    string ToErrorCode(Exception error);
}