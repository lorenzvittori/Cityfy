using CityFy.Orchestrator.Models;
using ServiceDefault.Contracts;
using ServiceDefault.Messaging;

namespace CityFy.Orchestrator.SubServices;

public sealed class CatalogRefreshRequestSubService(IMessagePublisher publisher) : ICatalogRefreshRequestSubService
{
    public Task PublishAsync(PipelineRun run, CancellationToken ct) =>
        publisher.PublishAsync(new RefreshCatalogRequested(run.RunId, run.UserId), correlationId: run.RunId, ct);
}