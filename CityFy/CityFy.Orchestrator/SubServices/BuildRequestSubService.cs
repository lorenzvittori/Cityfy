using CityFy.Orchestrator.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.SubServices;

public sealed class BuildRequestSubService(IMessagePublisher publisher) : IBuildRequestSubService
{
    public Task PublishAsync(PipelineRun run, CancellationToken ct) =>
        publisher.PublishAsync(new BuildCityRequested(run.RunId, run.UserId, run.Periods), correlationId: run.RunId, ct);
}
