using CityFy.CityGenerator.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.CityGenerator.SubServices;

public sealed class GenerationEventsSubService(IMessagePublisher publisher) : IGenerationEventsSubService
{
    public Task PublishRequestedAsync(GeneratedCity city, CancellationToken ct) =>
        publisher.PublishAsync(new GenerateCityRequested(city.CityId, city.UserId), city.CityId, ct);
}
