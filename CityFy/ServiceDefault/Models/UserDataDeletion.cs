namespace ServiceDefault.Models;

/// <summary>
/// Answer of DELETE internal/users/{userId}/data on every service that owns user data: what was
/// removed (name -> count). The user's data lives in several services; each one deletes its own.
/// </summary>
public sealed record UserDataDeletion(string Service, IReadOnlyDictionary<string, long> Deleted)
{
    /// <summary>"plays=12, artists=3", for the logs.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        foreach (var (what, count) in Deleted)
            parts.Add($"{what}={count}");
        return string.Join(", ", parts);
    }

    /// <summary>"Catalog plays=12, artists=3", for the logs.</summary>
    public string Describe() => $"{Service} {Summary()}";
}
