namespace CityFy.Elaboration.SubServices;

public interface ICityBuildErrorSubService
{
    /// <summary>
    /// ErrorCodes value of a failed build. MusicBrainz down/unreachable is told apart: the user can
    /// only wait, nothing is wrong with their data.
    /// </summary>
    string ToErrorCode(Exception error);
}
