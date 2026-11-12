namespace CityFy.Retrieve.Spotify.SubServices;

public interface IRetrieveErrorSubService
{
    /// <summary>ErrorCodes value shown to the user for a failed retrieve.</summary>
    string ToErrorCode(Exception error);
}