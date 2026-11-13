using System.Collections.Concurrent;
using CityFy.Api.Models;
using CityFy.Api.Repositories;

namespace CityFy.Api.Tests;

/// <summary>'users' in memory.</summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    public ConcurrentDictionary<string, CityFyUser> Users { get; } = new();

    public Task<CityFyUser?> GetAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);

    public Task<bool> RecordLoginAsync(string userId, string lastFmUsername, DateTime now, CancellationToken ct)
    {
        var created = Users.TryAdd(userId, new CityFyUser { Id = userId, LastFmUsername = lastFmUsername, CreatedAt = now, LastLoginAt = now });
        if (!created)
            Users[userId].LastLoginAt = now;
        return Task.FromResult(created);
    }

    public Task<long> DeleteAsync(string userId, CancellationToken ct) => Task.FromResult(Users.TryRemove(userId, out _) ? 1L : 0L);

    public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Users.Keys.ToList());
}
