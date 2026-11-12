namespace CityFy.CityGenerator.SubServices;

public interface IGenerationErrorSubService
{
    /// <summary>Stable error code of a failed generation (CITY_BUILD_FAILED when not a CityFyException).</summary>
    string ToErrorCode(Exception error);
}
