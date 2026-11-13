namespace CityFy.Catalog.SubServices;

public interface ICatalogRefreshErrorSubService
{
    /// <summary>
    /// ErrorCodes value of a failed catalog refresh. MusicBrainz down/unreachable is told apart: the user can
    /// only wait, nothing is wrong with their data.
    /// </summary>
    string ToErrorCode(Exception error);
}
