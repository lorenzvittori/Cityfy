using CityFy.Api.Models;

namespace CityFy.Api.SubServices;

/// <summary>Read-only access to the Mongo database 'cityfy' (admin panel). Values are not redacted.</summary>
public interface IMongoBrowseSubService
{
    Task<IReadOnlyList<TableInfo>> ListCollectionsAsync(CancellationToken ct);

    /// <summary>
    /// Newest documents first. The name is used only after matching it against the real collections.
    /// Throws NOT_FOUND (404) for an unknown collection.
    /// </summary>
    Task<TablePage> GetCollectionPageAsync(string name, int page, int pageSize, CancellationToken ct);
}
