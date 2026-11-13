namespace ServiceDefault.Models;

/// <summary>Body of POST internal/.../users/{fromUserId}/transfer: the new owner.</summary>
public sealed record UserDataTransferRequest(string ToUserId);

/// <summary>
/// Answer of a change of owner (e.g. anonymous session → account): what was moved (name → count).
/// Complete false: something could not be moved yet (e.g. an import still running); the transfer has
/// to be repeated later, it is idempotent.
/// </summary>
public sealed record UserDataTransfer(string Service, IReadOnlyDictionary<string, long> Moved, bool Complete = true);
