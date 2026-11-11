namespace ServiceDefault.Models;

/// <summary>
/// Answer of DELETE internal/users/{userId}/data on every service that owns user data: what was
/// removed (name -> count). The user's data lives in several services; each one deletes its own.
/// </summary>
public sealed record UserDataDeletion(string Service, IReadOnlyDictionary<string, long> Deleted);
