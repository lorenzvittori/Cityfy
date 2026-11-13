using System.Text.Json;

namespace CityFy.Api.Clients;

/// <summary>Admin: MusicBrainz catalog import (download + import of the dump, minutes to hours).</summary>
public interface IMusicBrainzServiceClient
{
    Task<JsonElement> GetImportStatusAsync(CancellationToken ct);

    /// <summary>Throws PROCESS_NOT_CANCELLABLE (409) when no import is running.</summary>
    Task<JsonElement> CancelImportAsync(CancellationToken ct);

    /// <summary>Throws MB_RETRIEVE_ALREADY_RUNNING (409) when an import is queued or running.</summary>
    Task<JsonElement> StartImportAsync(CancellationToken ct);
}
