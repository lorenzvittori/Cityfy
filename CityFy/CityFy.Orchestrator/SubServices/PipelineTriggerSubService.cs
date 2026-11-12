using CityFy.Orchestrator.Models;
using ServiceDefault.Contracts;

namespace CityFy.Orchestrator.SubServices;

public sealed class PipelineTriggerSubService : IPipelineTriggerSubService
{
    public const string CatalogSource = "catalog";
    public const string ManualSource = "manual";

    public PipelineTrigger? FromImport(ListeningImported message) =>
        message.NewPlays <= 0
            ? null
            : new PipelineTrigger { Source = message.Source, SourceRef = message.SourceRef, NewPlays = message.NewPlays, At = DateTime.UtcNow };

    public PipelineTrigger FromCatalog(CatalogUpdated message) =>
        new() { Source = CatalogSource, SourceRef = message.DumpName ?? "musicbrainz", NewPlays = 0, At = DateTime.UtcNow };

    public PipelineTrigger Manual() =>
        new() { Source = ManualSource, SourceRef = "admin", NewPlays = 0, At = DateTime.UtcNow };
}
