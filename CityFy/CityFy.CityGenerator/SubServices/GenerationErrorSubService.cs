using ServiceDefault.Errors;

namespace CityFy.CityGenerator.SubServices;

public sealed class GenerationErrorSubService : IGenerationErrorSubService
{
    public string ToErrorCode(Exception error) => error switch
    {
        CityFyException c => c.Code, // e.g. CITY_CATALOG_MISSING, SERVICE_UNAVAILABLE from the Catalog client
        _ => ErrorCodes.CityBuildFailed
    };
}
