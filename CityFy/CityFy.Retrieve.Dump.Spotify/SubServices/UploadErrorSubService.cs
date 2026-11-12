using ServiceDefault.Errors;

namespace CityFy.Retrieve.Dump.Spotify.SubServices;

public sealed class UploadErrorSubService : IUploadErrorSubService
{
    public string ToErrorCode(Exception error) => error is CityFyException cf ? cf.Code : ErrorCodes.UploadProcessingFailed;
}