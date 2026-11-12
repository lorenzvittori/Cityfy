using CityFy.CityGenerator.Models;

namespace CityFy.CityGenerator.SubServices;

public interface IGenerationEventsSubService
{
    /// <summary>GenerateCityRequested: the job runs in the consumer of this service.</summary>
    Task PublishRequestedAsync(GeneratedCity city, CancellationToken ct);
}
