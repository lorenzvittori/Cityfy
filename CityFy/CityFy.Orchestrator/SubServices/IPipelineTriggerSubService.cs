using CityFy.Orchestrator.Models;
using ServiceDefault.Contracts;

namespace CityFy.Orchestrator.SubServices;

/// <summary>What caused a run (pure mapping of the incoming events).</summary>
public interface IPipelineTriggerSubService
{
    /// <summary>Null when nothing new was imported: the city would not change.</summary>
    PipelineTrigger? FromImport(ListeningImported message);

    PipelineTrigger FromCatalog(CatalogUpdated message);

    /// <summary>Rebuild requested by the user or the admin, without new imports.</summary>
    PipelineTrigger Manual();
}
